# Duck Studio 2.1

## Project files in 2.1.3

New saves, exported workspaces and automatic recovery snapshots use `.duckproj`.
Legacy `.weftproj` and `.duck` workspaces still open. Saving one offers a new
`.duckproj` copy and leaves the original intact. Recovery retention includes old
and new snapshots together and preserves assets needed by retained versions.

Durty Cloth `.dctproj` files remain available in Open and the Durty import command.
This import uses the adjacent built FiveM resource; build in Durty Cloth Tool first.
Save imported work as `.duckproj`. Studio does not write the Durty Cloth project format.
The installer offers Open With for all four extensions and keeps other apps' defaults.

## Editor tools

- **Edit > Undo / Redo:** up to 100 project editing steps, including names, deleted items, texture replacements, tattoo changes, batch edits and optimization. Asset contents are captured independently. A restored image stays linked to its original source if that source still matches the saved content; changed artwork is restored as an independent working copy. New edits clear redo. Ctrl+Z / Ctrl+Y work in the workspace; text fields retain their native text undo.
- **Tools > UV tools:** overlay actual model UV triangles on each texture, switch to a labeled checker, select a surface, click the model to locate a triangle and export a UV overlay at the texture's resolution.
- **Tools > Outfit builder:** combine male or female clothing, project props and base components, choose variations, hide layers to inspect clipping and save multiple presets. Presets persist in projects, history and portable builds. Export uses the collection/local drawable order actually assigned by the builder.
- **Fitting animations:** Standing, Walking, Running, Sitting and Aiming in the separate clothing preview and outfit builder. Procedural joint rotations deform the real skin weights. The separate asset-only preview cannot animate clothing that relies on a skeleton stored in the base PED; fit it on the PED instead. These poses do not emulate GTA cloth physics or exact animation clips.
- **Tools > Build validation:** checks missing/corrupt YDD/YTDs, unresolved diffuse maps (custom-named clothing color overrides are supported; unresolved normal-only or ambiguous dictionaries are blocked), duplicate positions/names, component/variation limits, missing LODs, large textures, triangle budgets and stale outfit references. Double-click a drawable finding to select it. Export a report. Build errors block publishing; optimization warnings remain advisory.
- **Edit > Batch editing:** select with Ctrl/Shift, rename using `{name}`, `{slot}`, `{gender}`, `{n:000}`, assign positions by slot/gender, resize/compress selected textures and import an image folder as naturally ordered linked variations. Apply commits the draft; Cancel discards it.
- **Tools > Product screenshots:** clean PNGs with optional transparency, fixed lighting, front/back/left/right views, all variations or all items, and labeled contact sheets in pages of 24. Saved outfits can be captured together. Export creates a unique folder and supports cancellation.
- **Tools > Project history and comparison:** Ctrl-select two revisions to compare project settings, item/tattoo metadata, source artwork, content hashes, file sizes and outfit presets. Recovery assets must still be retained for both revisions.
- **Tools > Model optimization and LODs:** actual vertex/triangle/mesh counts and previews of each LOD; non-destructive Medium, Low and Very low generation using conservative vertex clustering. High geometry and original material/texture blocks stay intact. Open boundaries, material boundaries and skin weights are protected. Ratio goals can be limited by seams/topology. Generated distances are 30/60/120/180 metres; review every LOD before applying. Low-LOD YMT support is enabled when all component items in that gender's collection have generated LODs.

## FiveM test companion

FiveM builds include two resource folders: your clothing pack and `duckstudio_test`. Ensure the clothing resource first, then the companion. Run **/duckstudio**. Its browser selects clothing, variations, outfits and tattoos. It creates a local preview character and camera; it does not edit the player's appearance. Escape/Close removes the preview.

The companion uses collection-based clothing identifiers, exact GTA tattoo decoration natives and GTA walking/running/sitting/aiming tasks. Re-export and restart the companion after rebuilding or changing collection/positions. It is intended for a development server. Its source and generated files were checked locally; live FXServer integration still needs a server test.

The desktop tattoo preview shows artwork on the skin UV atlas. Use the companion to verify the game's actual decoration projection and clothing clipping.

## Existing features retained

PSD/PSB/photo drag and drop, linked texture updates, hover previews, automatic recovery backups, prompted closing, tattoo authoring, texture optimizers, history restoration, FiveM/alt:V/single-player/raw builds and the Duck Studio branding remain available.

The installer packages the updated renderer and .NET runtime. Updates continue to check automatically and ask before installation. New copies use the public signed GitHub Releases feed; custom signed feeds remain supported. See [RELEASING.md](../RELEASING.md).
