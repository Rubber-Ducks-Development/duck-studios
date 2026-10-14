# Duck Studio

Duck Studio is a Windows desktop editor for building GTA V freemode clothing collections for FiveM. It is intended to shorten the work between importing clothing files and producing a server resource. The application is written in WPF for .NET 10 and uses CodeWalker components for GTA asset and metadata work.

## What the app does

1. Create a project with a name, DLC collection name, and output folder before importing assets.
2. Import `.ydd` clothing models and `.ytd` texture variations from files, folders, or a built FiveM resource. Assign male or female freemode peds, component or prop type, slot, and position.
3. Edit names, texture ordering, audio preset text, behaviour flags, shop fields, creator details, optional first-person `.ydd`, and optional physics `.yld` files.
4. Preview a drawable in a separate 3D window. Component clothing can be fitted to a full PED when a local GTA V installation is linked. Drag to rotate and scroll to zoom.
5. Validate source files and build limits. A green lightbulb means the drawable passed its checks. A red lightbulb means it has a finding. Hover to read the finding or click the bulb to open findings for that drawable.
6. Open a prebuild window, then generate a FiveM resource with an `fxmanifest.lua`, binary clothing YMT files, shop META files, and streamed assets. ZIP creation is optional.

## Export layout

The normal export is an unzipped `<collection>-project` folder with exactly three top-level folders:

```text
<collection>-project/
  FiveM/
    <collection>/
      fxmanifest.lua
      *_shop.meta
      stream/
  Project/
    <collection>.weftproj
    creators.json
    weft-build.json
    README.txt
  Assets/
    original source files
```

Copy `FiveM/<collection>` into the server's resources directory and add `ensure <collection>` to `server.cfg`. Open the `.weftproj` file to edit or rebuild the exported project. The manifest includes a Duck Studio build comment. A ZIP, when requested, contains the same three folders.

## Current feature coverage

This comparison uses the [Durty Cloth Tool documentation index](https://docs.gta.clothing/llms.txt), [User Interface](https://docs.gta.clothing/basics/essential-knowledge/user-interface), [Build Options](https://docs.gta.clothing/basics/essential-knowledge/build-options), [Cloth Analysis Report](https://docs.gta.clothing/basics/essential-knowledge/cloth-analysis-report), and [Import and Export Formats](https://docs.gta.clothing/basics/useful-to-know/import-and-export-formats). It describes Duck Studio's actual implementation, not a claim of full Durty Cloth Tool compatibility.

| Area | Duck Studio status |
| --- | --- |
| Create, save, reopen, and relocate a project | Supported with the `.weftproj` format and bundled relative asset paths. |
| Freemode clothing components and props | Supported for 12 component slots and five prop anchors. |
| FiveM export | Supported with streamed files, binary YMT, shop META, and manifest. Rebuild and optional ZIP are supported. |
| 3D preview | Partial. Separate window, zoom, rotation, texture selection, backgrounds, and basic display options work. Full PED fitting requires a local GTA V installation. |
| Drawable analysis | Partial. Missing or invalid files, missing diffuse textures, slot issues, and collection limits are checked. Embedded diffuse textures in YDD files are recognized. Detailed geometry, shader, LOD, mipmap, and UV analysis is still absent. |
| Error list | Partial. Findings can be viewed for the whole project or scoped to one drawable. Severity filters, ignore rules, thumbnails, and quick fixes are still absent. |
| Search and filters | Partial. Combined search matches name, gender, position, slot, model filename, and texture filenames. A quick filter covers findings, gender, components, and props. Filter chips, saved presets, tags, fuzzy ranking, and performance sorts are still absent. |
| Context menus | Partial. Common rename, replace, export, copy, reveal, and preview actions work. Unsupported actions are visibly disabled. |
| Texture ordering | Up and down controls work. Drag and drop reordering and multi-selection are still absent. |
| DCT project and item formats | No direct `.dctpackage` or `.dctexport` interchange. A `.dctproj` can be opened only through an adjacent built resource that Duck Studio can import. |
| Tattoos, overlays, and dynamic tattoo configs | Not implemented. |
| alt:V, RageMP, and single-player RPF targets | Not implemented. The build window offers only FiveM. |
| Material, embedded texture, model, and texture optimizers | Not implemented. |
| Automatic backups | Every minute, after save/open, and before close. Keeps 20 recovery points with shared asset copies. Open Options > Open automatic backups to restore a project. |
| Project history and dockable layout | Not implemented. |

## Changes in this update

- The supplied pixel art hanger is the EXE icon and the icon for the main, preview, build, and settings windows. The original PNG is kept in the source assets.
- The build window target selector now has readable text on a dark background.
- Dropdowns fade open and their chevrons animate. The preview window keeps a light closed selector to match its menu.
- Drawables with missing external diffuse textures show a red bulb unless their YDD contains an embedded diffuse texture. The validator also checks file extensions.
- Clicking a status bulb opens that drawable's findings. Search now accepts multiple terms across more item fields, and a quick filter narrows the list by findings, gender, or item kind.
- Em dashes and en dashes were removed from application text and this project documentation.

## Requirements and verification

The runnable Windows folder requires the .NET 10 Desktop Runtime. The bundled CodeWalker, SharpDX, and `rage.exe` files are needed for metadata and previews. Build from source with the .NET 10 SDK using `dotnet build WeftStudio.csproj -c Release`.

Verification covers a warning-free Release build, WPF rendering of the main and build windows, icon and selector checks, embedded and missing diffuse texture cases, scoped findings, combined search, metadata round trips, project relocation and rebuild, and ZIP output. A live FiveM server test is still needed before deployment.

Duck Studio is independent software and is not affiliated with Durty Cloth Tool, Rockstar Games, or FiveM.


## October 13 preview and recovery update
The 3D scene exporter now uses the correct four-color RGB palette for DXT3/DXT5 blocks regardless of endpoint ordering, fixing black speckles and color shifts. Solid PED materials render opaque, fixing the see-through default shirt. Scene exports include shader metadata so materials retain their intended alpha behavior. Hover over the image icon beside a texture to see its thumbnail.
Automatic recovery snapshots run every minute when the project or referenced files change, after saving or opening, and before closing. Snapshots include asset copies, deduplicate unchanged content, and retain the latest 20 versions per project. Options > Open automatic backups opens the recovery location. Open a .weftproj inside its Project folder to restore a version. Project saves use an atomic file replacement. Closing the main app always asks for confirmation; unsaved changes offer Save and close, Close without saving, and Cancel.
