//! Composing a ped from its variation info, as CodeWalker's ped viewer does
//! (`World/Ped.cs`, `PedsForm.cs`): every one of the 12 component slots gets a
//! drawable out of the ped's `.ydd` (or the streamed per-component `.ydd` of
//! that name) and a diffuse texture out of its `.ytd` (or the streamed `.ytd`
//! of that name), named by the rules of `MCPVDrawblData.GetDrawableName` /
//! `GetTextureName`, and the texture replaces the drawable's `DiffuseSampler`
//! when it is drawn (`Renderer.RenderPedComponent` → `RenderDrawable`'s
//! `diffOverride`).

use anyhow::{bail, Context, Result};
use std::collections::{HashSet, HashMap};
use std::path::{Path, PathBuf};

use rage_formats::{encode_image, parse_ydd, parse_ymt, parse_ytd, rage_joaat, to_rgba_image, Drawable, ImageFormat, PedVariationInfo, TextureFormat, YtdTexture};
use rage_render::RenderPart;

use crate::index::{GameIndex, Parts};
use crate::rpf::GtaKeys;

fn encode_scene_texture(texture: &YtdTexture) -> Result<Vec<u8>> {
    let mut image = to_rgba_image(texture)?;
    let block_size = match texture.format {
        TextureFormat::DXT1 => 8,
        TextureFormat::DXT3 | TextureFormat::DXT5 => 16,
        _ => return encode_image(&image, ImageFormat::Png, 90),
    };
    correct_block_colors(image.as_mut(), &texture.pixel_data, texture.width as usize, texture.height as usize, block_size)?;
    encode_image(&image, ImageFormat::Png, 90)
}

// texture2ddecoder 0.1.2 applies BC1's three-color branch to BC2/BC3.
// BC2/BC3 always use four RGB colors, even when endpoint 0 <= endpoint 1.
// Restore the correct RGB palette without touching their separate alpha data.
fn correct_block_colors(pixels: &mut [u8], data: &[u8], width: usize, height: usize, block_size: usize) -> Result<()> {
    let blocks_x = width.div_ceil(4);
    if data.len() < blocks_x * height.div_ceil(4) * block_size || pixels.len() != width * height * 4 {
        bail!("scene texture pixel data is truncated");
    }
    fn rgb565(value: u16) -> [u8; 3] {
        [(((value >> 11) & 31) as u32 * 255 / 31) as u8,
         (((value >> 5) & 63) as u32 * 255 / 63) as u8,
         ((value & 31) as u32 * 255 / 31) as u8]
    }
    for by in 0..height.div_ceil(4) {
        for bx in 0..blocks_x {
            let offset = (by * blocks_x + bx) * block_size + block_size - 8;
            let q0 = u16::from_le_bytes(data[offset..offset + 2].try_into()?);
            let q1 = u16::from_le_bytes(data[offset + 2..offset + 4].try_into()?);
            let mut colors = [rgb565(q0), rgb565(q1), [0; 3], [0; 3]];
            for channel in 0..3 {
                let (c0, c1) = (colors[0][channel] as u16, colors[1][channel] as u16);
                if q0 > q1 || block_size == 16 {
                    colors[2][channel] = ((2 * c0 + c1) / 3) as u8;
                    colors[3][channel] = ((c0 + 2 * c1) / 3) as u8;
                } else { colors[2][channel] = ((c0 + c1) / 2) as u8; }
            }
            let selectors = u32::from_le_bytes(data[offset + 4..offset + 8].try_into()?);
            for py in 0..4 {
                for px in 0..4 {
                    let (x, y) = (bx * 4 + px, by * 4 + py);
                    if x >= width || y >= height { continue; }
                    let selected = ((selectors >> (2 * (py * 4 + px))) & 3) as usize;
                    let target = (y * width + x) * 4;
                    pixels[target..target + 3].copy_from_slice(&colors[selected]);
                    if block_size == 8 { pixels[target + 3] = if q0 <= q1 && selected == 3 { 0 } else { 255 }; }
                }
            }
        }
    }
    Ok(())
}

#[derive(clap::Args)]
pub struct SceneArgs {
    #[arg(long)]
    pub ped: String,
    #[arg(long, value_parser = parse_component)]
    pub component: Vec<(usize, SlotChoice)>,
    #[arg(long, requires = "override_ydd")]
    pub override_slot: Option<String>,
    #[arg(long, requires = "override_slot")]
    pub override_ydd: Option<PathBuf>,
    #[arg(long, requires = "override_ydd")]
    pub override_ytd: Option<PathBuf>,
    #[arg(long)]
    pub outfit: Option<PathBuf>,
    #[arg(short, long)]
    pub output: PathBuf,
}

#[derive(clap::Args)]
pub struct AssetSceneArgs {
    #[arg(long)]
    pub ydd: PathBuf,
    #[arg(long)]
    pub ytd: Option<PathBuf>,
    #[arg(short, long)]
    pub output: PathBuf,
}

pub fn run_asset_scene(args: &AssetSceneArgs) -> Result<()> {
    let bytes = std::fs::read(&args.ydd).with_context(|| format!("reading {}", args.ydd.display()))?;
    let drawable = parse_ydd(&bytes)?.into_iter().next()
        .context("the YDD has no drawable")?.drawable;
    let textures = if let Some(path) = &args.ytd {
        let bytes = std::fs::read(path).with_context(|| format!("reading {}", path.display()))?;
        parse_ytd(&bytes)?
    } else { Vec::new() };
    let override_texture = selected_diffuse(&drawable, &textures);
    let scene = Composed {
        name: args.ydd.file_stem().unwrap_or_default().to_string_lossy().into_owned(),
        slots: Vec::new(), textures: Vec::new(), warnings: Vec::new(),
        texture_overrides: [(0, textures)].into_iter().collect(),
        rigs: [(0, read_rigs(&bytes).ok().and_then(|r| r.into_values().next()).unwrap_or(json::JsonValue::Null))].into_iter().collect(),
        drawables: vec![(0, drawable, override_texture)],
    };
    scene.export_scene(&args.output)?;
    println!("Wrote textured asset scene to {}", args.output.display());
    Ok(())
}

pub fn run_scene(args: &SceneArgs, keys: Option<&GtaKeys>, exe: Option<&Path>) -> Result<()> {
    let index = GameIndex::load(exe, keys, Parts::PEDS).context("PED scene needs a GTA V installation supplied with --exe")?;
    let mut composed = compose(&index, keys, &args.ped, &args.component)?;
    if let (Some(slot_name), Some(ydd)) = (&args.override_slot, &args.override_ydd) {
        let slot = PedVariationInfo::slot_index(slot_name)
            .with_context(|| format!("'{slot_name}' is not a PED clothing slot"))?;
        composed.override_slot(slot, ydd, args.override_ytd.as_deref())?;
    }
    if let Some(path) = &args.outfit {
        let entries = json::parse(&std::fs::read_to_string(path)?)?;
        for entry in entries.members() {
            let slot_name = entry["slot"].as_str().context("outfit slot is required")?;
            let slot = PedVariationInfo::slot_index(slot_name).context("invalid outfit component slot")?;
            let model = entry["model"].as_str().context("outfit model is required")?;
            let texture = entry["texture"].as_str().map(Path::new);
            composed.override_slot(slot, Path::new(model), texture)?;
        }
    }
    composed.export_scene(&args.output)?;
    println!("Wrote textured PED scene to {}", args.output.display());
    Ok(())
}

/// What `--component SLOT=SPEC` asks for one slot.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum SlotChoice {
    /// Leave the slot out.
    None,
    /// Drawable, texture and alternative indices into the slot's variations
    /// (`PedsForm`'s combo box picks the same three).
    Pick { drawable: usize, texture: usize, alternative: usize },
}

impl SlotChoice {
    /// What `Ped.LoadDefaultComponents` picks for every slot.
    pub const DEFAULT: SlotChoice = SlotChoice::Pick { drawable: 0, texture: 0, alternative: 0 };
}

/// Parses `SLOT=D[:T[:A]]` or `SLOT=none` into the slot's index and choice.
pub fn parse_component(spec: &str) -> Result<(usize, SlotChoice), String> {
    let (slot_name, choice) = spec.split_once('=').ok_or_else(|| format!("expected SLOT=D[:T[:A]] or SLOT=none, got '{spec}'"))?;
    let slot = PedVariationInfo::slot_index(slot_name)
        .ok_or_else(|| format!("'{}' is not a component slot (one of {})", slot_name.trim(), rage_formats::PED_COMPONENT_NAMES.join(", ")))?;
    let choice = choice.trim();
    if choice.eq_ignore_ascii_case("none") {
        return Ok((slot, SlotChoice::None));
    }
    let mut numbers = choice.split(':').map(|n| n.trim().parse::<usize>().map_err(|_| format!("'{n}' is not an index in '{spec}'")));
    let drawable = numbers.next().ok_or_else(|| format!("missing drawable index in '{spec}'"))??;
    let texture = numbers.next().transpose()?.unwrap_or(0);
    let alternative = numbers.next().transpose()?.unwrap_or(0);
    if numbers.next().is_some() {
        return Err(format!("too many indices in '{spec}' (drawable:texture:alternative)"));
    }
    Ok((slot, SlotChoice::Pick { drawable, texture, alternative }))
}

/// What one slot ended up showing.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct SlotReport {
    pub slot: &'static str,
    /// The drawable's name, when the slot shows one.
    pub drawable: Option<String>,
    /// The texture's name, when the drawable takes one.
    pub texture: Option<String>,
}

impl SlotReport {
    /// `uppr: uppr_000_u / uppr_diff_000_a_whi`, or `uppr: none`.
    pub fn line(&self) -> String {
        match (&self.drawable, &self.texture) {
            (Some(d), Some(t)) => format!("{}: {d} / {t}", self.slot),
            (Some(d), None) => format!("{}: {d}", self.slot),
            (None, _) => format!("{}: none", self.slot),
        }
    }
}

/// A ped composed for rendering: its drawables, the texture each takes, and
/// every texture dictionary that took part.
pub struct Composed {
    /// The ped's name as `peds.ymt` spells it.
    pub name: String,
    pub slots: Vec<SlotReport>,
    /// Dictionaries to draw from: the ped's own `.ytd` first, then each
    /// streamed `.ytd` that was needed.
    pub textures: Vec<YtdTexture>,
    pub warnings: Vec<String>,
    drawables: Vec<(usize, Drawable, Option<String>)>,
    rigs: HashMap<usize, json::JsonValue>,
    // Keep each selected dictionary local to its clothing slot. Base PED
    // dictionaries can contain the same names with entirely different pixels.
    texture_overrides: HashMap<usize, Vec<YtdTexture>>,
}

fn selected_diffuse(drawable: &Drawable, textures: &[YtdTexture]) -> Option<String> {
    (0..drawable.shader_count() as u16)
        .filter_map(|id| drawable.diffuse_texture_name(id))
        .find_map(|name| textures.iter().find(|t| t.name.eq_ignore_ascii_case(name)))
        .or_else(|| textures.iter().find(|t| t.name.to_lowercase().contains("_diff_")))
        .or_else(|| textures.first()).map(|t| t.name.clone())
}

fn selected_texture<'a>(textures: &'a [YtdTexture], shader_name: Option<&str>, fallback: Option<&str>) -> Option<&'a YtdTexture> {
    shader_name.and_then(|name| textures.iter().find(|t| t.name.eq_ignore_ascii_case(name)))
        .or_else(|| fallback.and_then(|name| textures.iter().find(|t| t.name.eq_ignore_ascii_case(name))))
}

impl Composed {
    fn resolve_texture<'a>(&'a self, slot: usize, drawable: &'a Drawable, shader: Option<&str>, fallback: Option<&str>) -> Option<(&'a YtdTexture, &'static str)> {
        if let Some(textures) = self.texture_overrides.get(&slot) {
            selected_texture(textures, shader, fallback).map(|t| (t, "selected-ytd"))
        } else {
            fallback.or(shader).and_then(|name| self.textures.iter().find(|t| t.name.eq_ignore_ascii_case(name)))
                .map(|t| (t, "ped-dictionary"))
        }.or_else(|| shader.and_then(|name| drawable.shader_group.as_ref()?.textures.iter().find(|t| t.name.eq_ignore_ascii_case(name))).map(|t| (t, "embedded-model")))
    }
    pub fn export_scene(&self, output: &Path) -> Result<()> {
        std::fs::create_dir_all(output)?;
        let mut meshes = json::JsonValue::new_array();
        let mut written = HashSet::new();
        for (slot, drawable, override_texture) in &self.drawables {
            let rig = self.rigs.get(slot).cloned().unwrap_or(json::JsonValue::Null);
            let mut mesh_number = 0usize;
            let Some(lod) = drawable.best_lod() else { continue };
            for model in &lod.models {
                for geometry in &model.geometries {
                    let (Some(vertex_buffer), Some(indices)) = (&geometry.vertex_buffer, &geometry.index_buffer) else { continue };
                    let vertices = vertex_buffer.to_unified_vertices()?;
                    if vertices.is_empty() || indices.indices.is_empty() { continue; }
                    let mut positions = Vec::with_capacity(vertices.len() * 3);
                    let mut normals = Vec::with_capacity(vertices.len() * 3);
                    let mut uvs = Vec::with_capacity(vertices.len() * 2);
                    let mut weights = Vec::with_capacity(vertices.len() * 4);
                    let mut blend_indices = Vec::with_capacity(vertices.len() * 4);
                    for (vertex_index, vertex) in vertices.iter().enumerate() {
                        positions.extend([vertex.position.x, vertex.position.y, vertex.position.z]);
                        normals.extend([vertex.normal.x, vertex.normal.y, vertex.normal.z]);
                        uvs.extend([vertex.texcoord0.x, vertex.texcoord0.y]);
                        weights.extend([vertex.blend_weights.x,vertex.blend_weights.y,vertex.blend_weights.z,vertex.blend_weights.w]);
                        // Freemode often stores BlendIndices as Colour rather than
                        // UByte4; the unified parser otherwise silently returns zeros.
                        let attributes = vertex_buffer.read_vertex_attributes(vertex_index)?;
                        let ids = attributes.iter().find(|a| a.component.semantic == rage_formats::VertexSemantic::BlendIndices)
                            .map(|a| a.value.as_rgba8()).unwrap_or(vertex.blend_indices);
                        blend_indices.extend(ids);
                    }
                    let shader_name = drawable.diffuse_texture_name(geometry.shader_id);
                    let resolved = self.resolve_texture(*slot, drawable, shader_name, override_texture.as_deref());
                    let texture_file = resolved.and_then(|(texture, _)| {
                        let name = &texture.name;
                        let stem: String = name.chars().map(|ch| if ch.is_ascii_alphanumeric() || ch == '_' { ch } else { '_' }).collect();
                        let file = format!("slot{}_{}_{}.png", slot, stem, rage_joaat(&name.to_lowercase()));
                        if written.insert(file.clone()) {
                            match encode_scene_texture(texture) {
                                Ok(bytes) => { if std::fs::write(output.join(&file), bytes).is_err() { return None; } },
                                Err(_) => return None,
                            }
                        }
                        Some(file)
                    });
                    meshes.push(json::object! {
                        "slot": PedVariationInfo::slot_name(*slot),
                        "shader": drawable.shader_group.as_ref().and_then(|group| group.shaders.get(geometry.shader_id as usize)).map_or(0, |shader| shader.file_name_hash),
                        "render_bucket": drawable.shader_group.as_ref().and_then(|group| group.shaders.get(geometry.shader_id as usize)).map_or(0, |shader| shader.render_bucket),
                        "texture": texture_file,
                        "texture_name": resolved.map(|(t, _)| t.name.clone()),
                        "texture_origin": resolved.map(|(_, origin)| origin),
                        "shader_diffuse": shader_name,
                        "positions": positions,
                        "normals": normals,
                        "uvs": uvs,
                        "indices": indices.indices.clone(),
                        "weights": weights,
                        "blend_indices": blend_indices,
                        "bone_ids": rig["palettes"][mesh_number].clone(),
                        "bones": rig["bones"].clone(),
                    })?;
                    mesh_number += 1;
                }
            }
        }
        let bones = self.rigs.values().max_by_key(|r| r["bones"].len()).map(|r| r["bones"].clone()).unwrap_or(json::JsonValue::Null);
        let scene = json::object! { "ped": self.name.clone(), "meshes": meshes, "bones": bones };
        std::fs::write(output.join("scene.json"), scene.dump())?;
        Ok(())
    }
    /// One part per shown slot, each with its texture as the diffuse override,
    /// all turned to face the `front` view.
    pub fn render_parts(&self) -> Vec<RenderPart<'_>> {
        self.drawables
            .iter()
            .map(|(_, drawable, texture)| RenderPart { diffuse_override: texture.as_deref(), transform: facing_front(), ..RenderPart::new(drawable) })
            .collect()
    }

    /// Replace one composed slot with a loose add-on drawable and texture.
    pub fn override_slot(&mut self, slot: usize, ydd: &std::path::Path, ytd: Option<&std::path::Path>) -> Result<()> {
        if slot >= 12 { bail!("component slot is out of range"); }
        let bytes = std::fs::read(ydd).with_context(|| format!("reading {}", ydd.display()))?;
        self.rigs.insert(slot, read_rigs(&bytes).ok().and_then(|r| r.into_values().next()).unwrap_or(json::JsonValue::Null));
        let drawable = parse_ydd(&bytes)?.into_iter().next()
            .context("the add-on YDD has no drawable")?.drawable;
        let texture = if let Some(path) = ytd {
            let bytes = std::fs::read(path).with_context(|| format!("reading {}", path.display()))?;
            let loaded = parse_ytd(&bytes)?;
            let chosen = selected_diffuse(&drawable, &loaded);
            self.texture_overrides.insert(slot, loaded.clone());
            self.textures.extend(loaded);
            chosen
        } else { self.texture_overrides.remove(&slot); None };
        self.drawables.retain(|(current, _, _)| *current != slot);
        self.drawables.push((slot, drawable, texture.clone()));
        self.slots[slot].drawable = ydd.file_name().map(|name| name.to_string_lossy().into_owned());
        self.slots[slot].texture = texture;
        Ok(())
    }

    pub fn is_empty(&self) -> bool {
        self.drawables.is_empty()
    }
}

/// Ped meshes are modelled facing -Y, the opposite of vehicles, so the
/// `front` view (which looks at a vehicle's grille) would show a ped's
/// back: a half turn about Z puts the face in it.
fn facing_front() -> rage_formats::Mat4 {
    let mut m = rage_formats::Mat4::identity();
    m.0[0] = -1.0;
    m.0[5] = -1.0;
    m
}

/// Reads and parses one of the ped's files.
fn load<T>(index: &GameIndex, loc: &crate::index::EntryLoc, keys: Option<&GtaKeys>, parse: impl FnOnce(&[u8]) -> anyhow::Result<T>) -> Result<T> {
    let data = index.load_bytes(loc, keys)?;
    parse(&data).with_context(|| format!("failed to parse '{}'", loc.inner_path))
}

/// Composes `name` with the given slot choices (every other slot takes
/// `SlotChoice::DEFAULT`). The index must hold `Parts::PEDS`.
pub fn compose(index: &GameIndex, keys: Option<&GtaKeys>, name: &str, choices: &[(usize, SlotChoice)]) -> Result<Composed> {
    let hash = rage_joaat(&name.trim().to_lowercase());
    let Some(init) = index.ped_init.get(&hash) else {
        bail!("'{name}' is not a ped: no peds.ymt/peds.meta in the game lists it");
    };
    let Some(files) = index.ped_files.get(&hash) else {
        bail!("'{}' has no files: no {}.ymt with its dictionaries was found in the game's archives", init.name, name.trim().to_lowercase());
    };
    let Some(ymt) = &files.ymt else { bail!("'{}' has no .ymt", init.name) };

    let (info, _, _) = load(index, ymt, keys, |data| parse_ymt(data))?;
    let own_rigs = match &files.ydd {
        Some(loc) => read_rigs(&index.load_bytes(loc, keys)?).unwrap_or_default(),
        None => HashMap::new(),
    };
    let own_drawables = match &files.ydd {
        Some(loc) => load(index, loc, keys, |data| parse_ydd(data))?,
        None => Vec::new(),
    };
    let mut textures = match &files.ytd {
        Some(loc) => load(index, loc, keys, |data| parse_ytd(data))?,
        None => Vec::new(),
    };

    let mut composed = Composed { name: init.name.clone(), slots: Vec::new(), textures: Vec::new(), warnings: Vec::new(), drawables: Vec::new(), rigs: HashMap::new(), texture_overrides: HashMap::new() };
    let mut streamed_textures: Vec<YtdTexture> = Vec::new();

    for slot in 0..12 {
        let slot_name = PedVariationInfo::slot_name(slot);
        let choice = choices.iter().rev().find(|(s, _)| *s == slot).map_or(SlotChoice::DEFAULT, |(_, c)| *c);
        let mut report = SlotReport { slot: slot_name, drawable: None, texture: None };
        let SlotChoice::Pick { drawable, texture, alternative } = choice else {
            composed.slots.push(report);
            continue;
        };
        let Some(component) = info.component(slot) else {
            composed.slots.push(report);
            continue;
        };
        let Some(data) = component.drawables.get(drawable) else {
            if !component.drawables.is_empty() {
                composed.warnings.push(format!("{slot_name}: drawable {drawable} is out of range (the slot has {})", component.drawables.len()));
            }
            composed.slots.push(report);
            continue;
        };
        if alternative > data.num_alternatives as usize {
            composed.warnings.push(format!("{slot_name}: alternative {alternative} is out of range (the drawable has {})", data.num_alternatives));
        }
        let drawable_name = data.drawable_name(slot, drawable, alternative);
        let texture_name = data.texture_name(slot, drawable, texture);
        if texture_name.is_none() && !data.textures.is_empty() {
            composed.warnings.push(format!("{slot_name}: texture {texture} is out of range (the drawable has {})", data.textures.len()));
        }

        // The drawable: the ped's own dictionary, then a streamed file of that name.
        let drawable_hash = rage_joaat(&drawable_name.to_lowercase());
        let found = match own_drawables.iter().find(|entry| entry.hash == drawable_hash) {
            Some(entry) => Some(entry.drawable.clone()),
            None => match files.streamed_file(drawable_hash) {
                Some(loc) => load(index, loc, keys, |data| parse_ydd(data))?.into_iter().next().map(|entry| entry.drawable),
                None => None,
            },
        };
        let Some(found) = found else {
            composed.warnings.push(format!("{slot_name}: drawable {drawable_name} is in neither the ped's dictionary nor a streamed file"));
            composed.slots.push(report);
            continue;
        };

        // The texture: the ped's own dictionary, then a streamed file of that name.
        if let Some(tex) = &texture_name {
            let texture_hash = rage_joaat(&tex.to_lowercase());
            let held = textures.iter().chain(&streamed_textures).any(|t| t.name.eq_ignore_ascii_case(tex));
            if !held {
                match files.streamed_file(texture_hash) {
                    Some(loc) => streamed_textures.extend(load(index, loc, keys, |data| parse_ytd(data))?),
                    None => composed.warnings.push(format!("{slot_name}: texture {tex} is in neither the ped's dictionary nor a streamed file")),
                }
            }
        }

        let rig = if let Some(rig) = own_rigs.get(&drawable_hash) { rig.clone() }
            else if let Some(loc) = files.streamed_file(drawable_hash) {
                read_rigs(&index.load_bytes(loc, keys)?).ok().and_then(|r| r.into_values().next()).unwrap_or(json::JsonValue::Null)
            } else { json::JsonValue::Null };
        composed.rigs.insert(slot,rig);
        report.drawable = Some(drawable_name);
        report.texture = texture_name.clone();
        composed.drawables.push((slot, found, texture_name));
        composed.slots.push(report);
    }

    textures.extend(streamed_textures);
    composed.textures = textures;
    Ok(composed)
}

#[cfg(test)]
mod tests {
    use super::*;

    fn texture(name: &str, pixel: u8) -> YtdTexture {
        YtdTexture { name: name.into(), name_hash: rage_joaat(name), width: 1, height: 1, depth: 1, format: TextureFormat::A8R8G8B8, levels: 1, stride: 4, pixel_data: vec![pixel; 4] }
    }
    fn drawable() -> Drawable {
        use rage_formats::{DrawableBounds, ShaderGroup, ShaderFx, ShaderParameter, ShaderParameterValue, Vec3};
        let zero = Vec3 { x: 0., y: 0., z: 0. };
        Drawable { name: "test".into(), name_hash: 0, bounds: DrawableBounds { center: zero, sphere_radius: 1., box_min: zero, box_max: zero }, lod_distances: [1.;4], render_masks: [0;4], lods: vec![],
            shader_group: Some(ShaderGroup { textures: vec![texture("cloth_diff_000_a_uni", 30)], shaders: vec![ShaderFx { name_hash: 0, file_name_hash: 0, render_bucket: 0, render_bucket_mask: 0, parameter_count: 1, texture_parameter_count: 1, parameters: vec![ShaderParameter { name_hash: rage_joaat("DiffuseSampler"), data_type: 0, data_pointer: 0, value: ShaderParameterValue::Texture { name: "cloth_diff_000_a_uni".into(), name_hash: rage_joaat("cloth_diff_000_a_uni") } }] }] }) }
    }
    #[test]
    fn selected_dictionary_matches_diffuse_instead_of_first_entry() {
        let d = drawable();
        let textures = vec![texture("normal_map", 10), texture("cloth_diff_000_a_uni", 20)];
        assert_eq!(selected_diffuse(&d, &textures).as_deref(), Some("cloth_diff_000_a_uni"));
        assert_eq!(selected_texture(&textures, Some("CLOTH_DIFF_000_A_UNI"), Some("normal_map")).unwrap().pixel_data, vec![20;4]);
    }
    #[test]
    fn selected_slot_pixels_win_over_base_and_embedded_names_without_leaking() {
        let d = drawable();
        let scene = Composed { name: "test".into(), slots: vec![], warnings: vec![], textures: vec![texture("cloth_diff_000_a_uni", 10)], drawables: vec![], rigs: HashMap::new(), texture_overrides: [(3, vec![texture("cloth_diff_000_a_uni", 20)]), (4, vec![texture("cloth_diff_000_a_uni", 40)])].into_iter().collect() };
        for (slot, expected, origin) in [(3,20,"selected-ytd"),(4,40,"selected-ytd"),(5,10,"ped-dictionary")] {
            let (actual, source) = scene.resolve_texture(slot, &d, Some("cloth_diff_000_a_uni"), None).unwrap();
            assert_eq!(actual.pixel_data, vec![expected;4]); assert_eq!(source, origin);
        }
        assert!(scene.resolve_texture(3, &d, Some("missing"), None).is_none());
    }

    #[test]
    fn scene_bc3_keeps_four_colors_for_ascending_endpoints() {
        let mut block = vec![0u8; 16];
        block[8..10].copy_from_slice(&0x001fu16.to_le_bytes());
        block[10..12].copy_from_slice(&0xf800u16.to_le_bytes());
        block[12..16].copy_from_slice(&0xeeeeeeeeu32.to_le_bytes()); // selectors 2,3,2,3
        let mut pixels = vec![127u8; 64];
        correct_block_colors(&mut pixels, &block, 4, 4, 16).unwrap();
        assert_eq!(&pixels[0..4], &[85, 0, 170, 127]);
        assert_eq!(&pixels[4..8], &[170, 0, 85, 127]);
        // Descending endpoints must still use the same four-color model.
        block[8..10].copy_from_slice(&0xf800u16.to_le_bytes());
        block[10..12].copy_from_slice(&0x001fu16.to_le_bytes());
        correct_block_colors(&mut pixels, &block, 4, 4, 16).unwrap();
        assert_eq!(&pixels[0..4], &[170, 0, 85, 127]);
    }

    #[test]
    fn scene_bc1_keeps_transparent_selector_and_handles_partial_blocks() {
        let mut block = vec![0u8; 8];
        block[0..2].copy_from_slice(&0x001fu16.to_le_bytes());
        block[2..4].copy_from_slice(&0xf800u16.to_le_bytes());
        block[4..8].copy_from_slice(&u32::MAX.to_le_bytes());
        let mut pixels = vec![255u8; 2 * 3 * 4];
        correct_block_colors(&mut pixels, &block, 2, 3, 8).unwrap();
        assert!(pixels.chunks_exact(4).all(|pixel| pixel == [0, 0, 0, 0]));
        assert!(correct_block_colors(&mut pixels, &block[..7], 2, 3, 8).is_err());
    }

    #[test]
    fn component_specs_name_a_slot_and_up_to_three_indices() {
        assert_eq!(parse_component("uppr=2"), Ok((3, SlotChoice::Pick { drawable: 2, texture: 0, alternative: 0 })));
        assert_eq!(parse_component("HAIR = 1:2"), Ok((2, SlotChoice::Pick { drawable: 1, texture: 2, alternative: 0 })));
        assert_eq!(parse_component("jbib=0:1:3"), Ok((11, SlotChoice::Pick { drawable: 0, texture: 1, alternative: 3 })));
        assert_eq!(parse_component("berd=none"), Ok((1, SlotChoice::None)));
        assert_eq!(parse_component("berd=NONE"), Ok((1, SlotChoice::None)));
        assert!(parse_component("uppr").is_err());
        assert!(parse_component("hat=1").unwrap_err().contains("not a component slot"));
        assert!(parse_component("uppr=a").is_err());
        assert!(parse_component("uppr=1:2:3:4").unwrap_err().contains("too many"));
        assert!(parse_component("uppr=").is_err());
    }

    #[test]
    fn slot_lines_read_like_the_viewer() {
        let full = SlotReport { slot: "uppr", drawable: Some("uppr_000_u".into()), texture: Some("uppr_diff_000_a_whi".into()) };
        assert_eq!(full.line(), "uppr: uppr_000_u / uppr_diff_000_a_whi");
        let bare = SlotReport { slot: "accs", drawable: Some("accs_001_u".into()), texture: None };
        assert_eq!(bare.line(), "accs: accs_001_u");
        let none = SlotReport { slot: "berd", drawable: None, texture: None };
        assert_eq!(none.line(), "berd: none");
    }

    #[test]
    fn composing_an_unknown_ped_says_so() {
        let index = GameIndex::default();
        let err = match compose(&index, None, "nobody", &[]) {
            Ok(_) => panic!("an unknown ped composed"),
            Err(e) => e.to_string(),
        };
        assert!(err.contains("not a ped"), "{err}");
    }
}

// Preserve the rig omitted by the high-level drawable parser. The editor uses
// these palettes and bind transforms for procedural fitting poses.
fn read_rigs(bytes: &[u8]) -> Result<HashMap<u32,json::JsonValue>> {
    use rage_formats::blocks::{Reader,Graph,drawable::{Drawable as BlockDrawable,DrawableModelsBlock,DrawableModel,DrawableGeometry},skeleton::{Skeleton,SkeletonBonesBlock,Bone},base::StringBlock};
    use rage_formats::resource::{prepare_rsc7,ResReader,SYSTEM_BASE};
    let (sys,gfx)=prepare_rsc7(bytes)?;
    let reader=ResReader{system:&sys,graphics:&gfx};
    let header=reader.resolve(SYSTEM_BASE,0x40).context("YDD header missing")?;
    let u64at=|o|u64::from_le_bytes(header[o..o+8].try_into().unwrap());
    let u16at=|o|u16::from_le_bytes(header[o..o+2].try_into().unwrap());
    let pointers=reader.read_u64_list(u64at(0x30),u16at(0x38) as usize).context("YDD pointers missing")?;
    let hashes=reader.read_u32_list(u64at(0x20),u16at(0x28) as usize).unwrap_or_default();
    let mut r=Reader::open(bytes)?;let mut g=Graph::new();let mut result=HashMap::new();
    for (i,pointer) in pointers.into_iter().enumerate() {
        if pointer==0 {continue;}
        let Some(id)=BlockDrawable::read(&mut r,&mut g,pointer)? else {continue;};
        let drawable=g.get::<BlockDrawable>(id);let mut bones=json::JsonValue::new_array();let mut palettes=json::JsonValue::new_array();
        if let Some(skeleton)=drawable.skeleton {
            if let Some(bone_block)=g.get::<Skeleton>(skeleton).bones {
                for bone in &g.get::<SkeletonBonesBlock>(bone_block).bones {
                    let b=g.get::<Bone>(*bone);
                    bones.push(json::object!{"name":b.name.map(|n|g.get::<StringBlock>(n).0.clone()).unwrap_or_default(),"index":b.index,"parent":b.parent,"tag":b.tag,"translation":vec![b.translation.x,b.translation.y,b.translation.z],"rotation":vec![b.rotation.x,b.rotation.y,b.rotation.z,b.rotation.w],"scale":vec![b.scale.x,b.scale.y,b.scale.z]})?;
                }
            }
        }
        if let Some(block)=drawable.models {
            let models=g.get::<DrawableModelsBlock>(block);
            if let Some(level)=models.high.as_ref().or(models.med.as_ref()).or(models.low.as_ref()).or(models.vlow.as_ref()) {
                for model in level {for geometry in &g.get::<DrawableModel>(*model).geometries {palettes.push(g.get::<DrawableGeometry>(*geometry).bone_ids.clone())?;}}
            }
        }
        result.insert(hashes.get(i).copied().unwrap_or(0),json::object!{"bones":bones,"palettes":palettes});
    }
    Ok(result)
}
