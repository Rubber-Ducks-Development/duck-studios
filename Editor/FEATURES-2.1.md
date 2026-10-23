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

Imported YTD image replacements preserve dictionary lookup keys and keep the edited color texture first for streamed clothing. Builds refresh linked images before exporting each variation; original DCT resources remain unchanged.

## Replacement packs

Build Project > **Pack mode (advanced)** chooses how the clothing collection is named:

- **Add-on (own collection):** the default. Clothing is built into `mp_m_<DLC name>` / `mp_f_<DLC name>`.
- **Replace a GTA DLC collection:** pick a DLC from your GTA V installation, for example mpheist (`mp_m_heist` / `mp_f_heist`). Scan GTA reads the freemode YMT names from every pack under `update/x64/dlcpacks`, so the list matches your game build. Link GTA5.exe in Settings first.
- **Replace a custom collection:** enter the male and female collection names yourself, for example to replace another resource's add-on pack.

Add and edit clothing as usual. The build streams the YMT, models and textures under the replaced collection's names (`mp_m_freemode_01_mp_m_heist.ymt`, `mp_m_freemode_01_mp_m_heist^jbib_000_u.ydd` and so on), so your items become drawables 0, 1, 2... of that collection in position order and appear where the original collection sits in clothing menus, not at the end. Replacement packs have no shop meta or `data_file` lines: registering the DLC's collection name again would make GTA add a second collection after every DLC instead of replacing the original.

A replacement pack takes over the complete collection, not single items. The original items are no longer available, and a collection with fewer drawables than the original leaves the higher numbers empty. Deploy the whole resource and test each gender on your server's game build. Replacement packs build for FiveM and raw game assets; alt:V and single-player builds are refused.

Builds run in the background. The footer shows the current phase or file while editing is paused; the window keeps its normal colors and continues to repaint. Choose **Cancel build** or press **Escape** to stop. Artwork conversion uses independent build textures, and optional ZIP compression also runs off the UI thread. Incomplete ZIPs are removed when cancelled; completed exports remain available.

Model validation reads binary geometry counts directly and reuses inspection results within a pass. It runs in a separate headless process, so native decoding and large temporary allocations cannot suspend the editor's renderer. Per-model and per-texture status identifies the current input. Cancellation terminates the worker, and a two-minute timeout reports the last step if validation exceeds its limit.

DLC discovery shows elapsed scan time and offers **Stop scan**. Closing the build dialog or changing away from DLC replacement stops its reader. Scans time out after five minutes. Successful discovery is cached across app launches and rechecked against the game executable, reader and DLC archive inventory; **Scan GTA** forces a fresh read. Installations with linked DLC folders bypass the cache so changes inside those folders cannot leave stale results.

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
