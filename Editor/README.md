# Duck Studio editor guide

[Project overview and downloads](../README.md) · [Feature guide](FEATURES-2.1.md) · [Release notes](../CHANGELOG.md)

Windows editor for GTA V / FiveM freemode clothing collections. See FEATURES-2.1.md for undo/redo, UV inspection, saved outfits, fitting poses, batch editing, model LODs, screenshots, revision comparison and the FiveM test companion.

Public source and downloads: https://github.com/Rubber-Ducks-Development/duck-studios. Read SECURITY.md in the source repository for private vulnerability reporting.

## Run

Open `DuckStudio-Windows/DuckStudio.exe` with the rest of that folder intact. The Windows release includes the .NET 10 Desktop Runtime. To build source, use the .NET 10 SDK and `dotnet build WeftStudio.csproj -c Release`. Metadata generation and previews use the bundled CodeWalker DLL, SharpDX DLLs, and `rage.exe`.

## Workflow

1. Create a project with a name, DLC / collection name, and output folder before importing assets. GTA5.exe is needed only for previews fitted to a PED.
2. Import YDD models and YTD textures from files, folders, or an existing FiveM resource. Choose male or female, component or prop, and the actual GTA slot. The editor supports all 12 component IDs (face, mask, hair, torso, legs, bags, shoes, neckwear, undershirt, armor, decals, tops) and five freemode prop anchors (hats, glasses, ears, watches, bracelets).
3. Edit textures, audio, behaviour flags, shop metadata, creator details, optional first-person models, and YLD physics. Click anywhere on a **Select a file** field to open a filtered Windows file picker in the model's folder. Clothing rows and texture rows have right-click menus for rename, replace, export, copy and preview actions. A green lightbulb means that drawable passed its checks; a red bulb marks a finding. Hover to read the reason or click the bulb to open that drawable's findings. A YDD with an embedded diffuse texture can pass without an external YTD. Actions the editor cannot safely perform yet are shown disabled.
4. Open **3D Preview Cloth** to inspect the model in a separate window. With GTA5.exe linked, component clothing opens on a full PED; prop clothing opens by itself. Drag to rotate and scroll to zoom. The preview menu offers textured or solid rendering, background color, light direction, grid, floor, camera rotation, a screenshot action, and model and texture information. Display changes animate briefly; repeated texture selections cancel stale scene loads.
5. Click **Build project** or **Project > Export Project** to review the collection name, output folder, stream grouping and optional ZIP choice in a prebuild dialog. The normal export is an ordinary, unzipped folder.

Dropdowns use high-contrast colors and short opening animations. The drawable search accepts combined terms for name, position, gender, slot, model file, and texture file. A quick filter shows only findings, one gender, components, or props.

Each clothing category uses one list, with male items first and female items below them. Added models, resource items and gender copies take the next position for their gender and clothing type. Select a texture and double-click its row (or choose **3D Preview Cloth**) to preview that exact variation. YTD files can be added after their models; use **Add texture** on a selected drawable for custom filenames or ambiguous matches. Dragging right rotates the model right by default. **Invert horizontal panning** is available in Project Settings and the 3D preview's Options tab, and is saved across sessions. Preview tab headers stay in place when selected.

New clothing rows use a light yellow background for two seconds, then fade back to normal over 0.7 seconds. Each import or gender copy has its own timer, which continues through selection and list refreshes. No click is required.

Adding clothing reveals the final new item once after the complete batch. Its category opens, and a search or filter is cleared if it would hide the addition. The clothing action buttons fit the standard pane and wrap onto another row when narrowed.

The default export is `<output>/<collection>-project/` with exactly these three folders:

```text
<collection>-project/
  FiveM/
    duckstudio_test/           Optional in-game clothing/outfit/tattoo test browser
    <collection>/              Copy this folder to your server resources directory
      fxmanifest.lua
      *_shop.meta
      stream/                   YDD, YTD, binary clothing YMT, creature YMT, optional YLD
        <ped>_<collection>_<slot>/   Optional grouped component files
        <ped>_p_<collection>_<slot>/ Optional grouped prop files
  Project/
    <collection>.duckproj           Open this file in Duck Studio
    creators.json
    weft-build.json
    README.txt
  Assets/                       Original input files for editing and rebuilding
```

Add `ensure <collection>` to your server.cfg after copying `FiveM/<collection>` to the server's resources directory. The ZIP option contains the same three folders at its root. A bundled project uses relative asset paths, including after saving and moving the whole export. Build replaces only an existing export that Duck Studio created; validation failure preserves the previous export.

The builder assigns contiguous local drawable indexes per gender and slot in Position order. It writes a CPedVariationInfo YMT for each gender, one ShopPedApparel META per gender, and registers the META files in the manifest with `SHOP_PED_APPAREL_META_FILE`. The generated `fxmanifest.lua` starts with `-- Built with Duck Studio`. Optional supplemental YMT files are copied to stream. Custom facial expressions and shader tracks are not authored by the current editor. Server-side FiveM runtime testing is still recommended.

## Format references

- [FiveM resource layout](https://docs.fivem.net/docs/scripting-manual/introduction/introduction-to-resources/) and [resource manifest](https://docs.fivem.net/docs/scripting-reference/resource-manifest/)
- [FiveM data file types](https://docs.fivem.net/docs/game-references/data-files/) for `SHOP_PED_APPAREL_META_FILE`
- [FiveM collection-based drawable and prop indexes](https://docs.fivem.net/docs/scripting-manual/using-new-game-features/collection-based-natives/)
- [CodeWalker GTA component and anchor enums](https://github.com/dexyfex/CodeWalker/blob/master/CodeWalker.Core/GameFiles/MetaTypes/MetaTypes.cs)

## Verification

The release compiles without warnings or errors. QA round-tripped male and female binary YMT metadata through CodeWalker, checked multi-anchor props and texture variations, grouped stream files, the manifest watermark, protected an existing export on validation failure, rebuilt from the bundled project, saved and relocated that project, and checked optional ZIP contents. WPF checks verified that the icon assets load, both asset fields are buttons with file-type filters and a sensible initial folder, and the drawable status icon and hover text change with validation. A rendered project window was checked at full resolution for icon clarity. The Windows app was previously visually checked at close zoom and rotation with the vest alone and on a full PED; both views displayed coherent mesh and texture. The right-click menus and prebuild dialog were inspected in the running app, and the dialog successfully built the verification project. A live FiveM server was not available for runtime validation.

## Credits

The user supplied the pixel art duck logo in `Assets/AppIconSource.png`; `Assets/AppIcon.ico` contains the Windows icon sizes used by the EXE and windows. The interface icon set in `Assets/Icons` and editable SVG originals in `Assets/IconSources` came from the user's `remade_logos` folder. Duck Studio source is MIT licensed. See `THIRD_PARTY.md` for CodeWalker and the renderer. Duck Studio is not affiliated with Durty Cloth Tool, Rockstar Games, or FiveM.


## October 13 fixes
The 3D scene exporter now decodes the DXT3/DXT5 RGB palette with four colors for both endpoint orderings, fixing black speckles and color changes. Solid PED clothing renders opaque, fixing the translucent shirt. Shader metadata preserves cutout and blended materials. Hover over a texture photo icon to inspect a thumbnail.
Automatic backups run every minute when project state or referenced files change, after open/save, and before closing. Options > Open automatic backups opens the backup root. Open a .duckproj inside a backup Project folder to restore it. The newest 20 recovery points are kept per project; asset copies are shared when unchanged. The app asks before closing and offers Save and close, Close without saving, and Cancel when changes are unsaved. Project saves use atomic replacement.


## Duck Studio 2.1.2 rebrand and panel dismissal
Duck Studio uses the supplied transparent duck logo in its executable and window icons. The executable is DuckStudio.exe. The current workspace extension is .duckproj. Legacy .weftproj and .duck workspaces still open; saving offers a .duckproj copy and preserves the original. Settings, renderer caches, and automatic backups remain part of the editor workflow.
Press Escape or click a non-interactive area of a temporary right-side view to return to the drawable editor. This covers the Build/Error List view, Project settings, the embedded preview, texture overlays, and secondary detail tabs. Buttons, text fields, dropdowns, scroll controls, and the 3D viewport keep their normal interaction. Escape also closes the standalone clothing preview and cancels the build/settings dialogs.

## Linked photos and Photoshop textures (1.2)

Drop an image onto a texture row to replace that variation. Drop into empty space in the texture list to add a variation. Add texture and Replace texture also accept images. PNG, JPG/JPEG, PSD/PSB, WebP, BMP, TIFF, GIF, TGA, DDS, HEIC/HEIF, AVIF, ICO, JPEG XL, JPEG 2000, PCX, portable bitmap formats, QOI, EXR and HDR are accepted. Animated and multi-page files use their first image. PSD uses its saved merged composite, so save the document in Photoshop to publish your changes. No Photoshop installation is required by Duck Studio.

Source photos stay linked to their original paths. The app checks every second and waits for a stable save before regenerating a real YTD with the model's diffuse texture name, preserved extra texture maps, alpha and mipmaps. Interrupted or unreadable saves keep the last good texture and retry automatically. The texture view, hover thumbnail and both 3D previews update; open 3D windows retain rotation and zoom. Linked variations are marked in the list, and Reveal file opens the original image's location.

Images are converted to power-of-two texture dimensions, up to 8192 pixels per side, with lossless 8-bit BGRA texture data. Keep source files in place, or use Replace texture to relink them. Project files remember the links. Builds read the latest source save and bundle the source images in Assets alongside the generated YTDs. Recovery backups include both. Reopening an exported project or recovery point uses independent working YTD copies so editing does not overwrite shared recovery textures. The Windows release is x64 and includes its image decoder.

The supplied replacement duck artwork is used unchanged as the window/logo PNG and packed into standard Windows icon sizes for the executable.

## Studio 2 features

Tools > Tattoos imports linked PNG/JPG/PSD/PSB/YTD artwork, supports male, female and shared presets, body zones, facing, UV centers, scale, rotation, price and update groups. Drag on the UV grid to reposition artwork. The optional PED window previews it on the skin UV atlas; GTA's native decoration projection must be verified in-game. Builds create a 512px DXT5 YTD per image, overlays XML, shop_tattoo.meta, and tattoos.json. Tattoo-only packs are supported. Saving source artwork refreshes its preview and generated texture. Existing YTD tattoo imports require one image.

Tools > Project history records saves, builds, manual checkpoints and changed automatic recovery points. Entries show what changed. Restore first captures the current state and opens independent copies of every source asset; it does not overwrite the original saved project. Save As to keep that restored working copy. The newest 100 asset snapshots and up to 300 history events are retained per project, independently of the 20 automatic recovery backups.

Tools > Optimizers analyzes maximum texture dimensions and duplicate content. Choose a size cap, optional DXT5 compression with mipmaps, and removal of identical variations. Conversion completes before the editor switches to optimized copies. Original source files remain untouched; history can restore the previous list. Normal/specular/ATI/BC7 data maps are preserved. Linked photo settings remember the optimization cap and compression choice for future saves. Removing duplicate variations renumbers the remaining variations.

Build Project now selects FiveM, alt:V, single-player DLC or raw assets. The output contains the selected deployment folder, Project and Assets. alt:V uses resource.toml, stream.toml and virtual RPF clothing folders. Single-player produces a real nested RPF7 dlc.rpf with setup2.xml/content.xml, registered clothing and tattoo metadata, and installation instructions. Freemode add-on clothes/tattoos need a trainer/script to select them in single-player; story-character replacement clothes are not generated. Raw assets provide the game files without a server resource wrapper. FiveM and alt:V runtime testing and a single-player game launch were not performed in this build; metadata, asset bundling and archive structure were checked locally.

Help > Update settings enables automatic checks on startup and every six hours. A newer signed release opens an Install update / Not now prompt. Downloads must match the signed size and SHA256; failed downloads retain the last verified installer. Installation starts only after consent and after the app completes saving/recovery and closes. Cancelling close also cancels the pending install. HTTPS and local/shared signed feeds are supported. The release folder contains UpdateFeed/latest.json for local updates; online delivery requires publishing the signed feed and installer to your chosen host. A local release feed remains linked after installation.

Installer/DuckStudioSetup.exe installs for the current Windows account, creates a Start menu shortcut, adds an uninstall entry and registers Open With for project files. It includes the runtime and rejects unrelated nonempty target folders. Updates keep unknown project files and a previous installation for recovery. Uninstall removes the app's recorded files and keeps user projects, history and backups. Windows publisher signing requires the maintainer's own Authenticode certificate; release-feed signing is included.

The updater runs outside the installation folder and releases inherited working-directory locks before replacing files, including when launched by older Duck Studio versions. Temporary sharing locks are retried briefly. If installation still fails, the error identifies the locked path and provides a diagnostic log under `%LOCALAPPDATA%\DuckStudio\Logs`.

## Studio 2 verification

44 integration checks covered tattoo conversion/alpha/presets, all four targets, real RPF7 creation, source bundling, history restore independence, optimizer copies/downsize/deduplication, release signatures, corrupt-download rejection, atomic installation, preservation of personal projects and scoped uninstall. All new tool windows were rendered and inspected. Existing material, backup and panel regression checks remain part of the release workflow.


### Preview troubleshooting

Use Help > Preview debug report after reproducing a texture mismatch. Refresh, then copy or save the report for support. It records selected file names and linked artwork, renderer failures, and the resolved texture name/source for each material. The Windows profile prefix is redacted; review filenames before sharing. Reports contain no model or image data. Recent events are bounded and stay in memory until a report is saved.

Drag or scroll anywhere on the preview background to rotate or zoom. From photos is available beside Menu, in Textures data, and in the main 3D Preview sidebar. Imported artwork becomes a linked YTD variation and is selected for preview. The blue background preset is named Duck Blue.


Clothing categories keep their open/closed state while adding clothes or filtering the list. New additions scroll into view; if their category is closed, its header is revealed without reopening it. All 3D previews support close detail zoom with proportional wheel movement and adaptive near clipping. Opening the preview menu or resizing the main preview preserves close zoom.


Build failures open a resizable, scrollable Build report instead of a long message box. Use the mouse wheel, scroll bars or Ctrl+End to reach every finding; Copy report and Save report include the complete text. The latest build failure also appears in Help > Preview debug report. Missing diffuse findings show expected and available texture names; send the saved report and matching source assets to support. Same-version patch builds retain the public version and can be distinguished by the Build line in debug reports.
