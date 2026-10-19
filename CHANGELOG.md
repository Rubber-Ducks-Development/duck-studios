# Changelog

## 2.1.10

### Report scrolling patch (same version)

- Replace oversized build error message boxes with a bounded, resizable report supporting wheel/keyboard scrolling, horizontal scrolling, copy, save and close.
- Use the same report layout in Help debug, preserve its scroll position on refresh, and include the latest full build failure.
- Preserve structured build findings and show the available YTD/embedded texture names alongside missing diffuse maps.
- Keep texture validation in place; source asset problems require matching model/texture files.
- Keep the public version and existing release; identify patch builds by source revision in diagnostic/build metadata.

- Preserve each clothing category's open/closed state across single and bulk additions, changing counts, searches and list refreshes.
- Reveal additions in open categories normally; for a closed category, scroll to its header without reopening it.
- Allow much closer 3D zoom across standalone, embedded, outfit/UV and tattoo previews, with proportional wheel deltas and a smaller near clipping plane for detail inspection.
- Keep close zoom when resizing the main preview or opening the preview menu.

## 2.1.9

- Add Help > Preview debug report with selected variation, linked artwork, renderer errors and actual scene texture bindings. Reports can be refreshed, copied or saved without model/image contents.
- Match each mesh to its diffuse texture in the selected YTD and keep selected clothing dictionaries separate from base PED dictionaries with matching texture names.
- Match texture thumbnails to the linked artwork/model diffuse name and clear stale 3D visuals while loading another selection.
- Rename Durty blue to Duck Blue and allow orbit/zoom from the entire preview background.
- Add From photos to standalone and embedded 3D previews, using the existing linked-image importer and selecting the imported variation.

## 2.1.8

- Launch the updater with a neutral working directory and release inherited installation-folder handles before replacement, including updates launched by older editors.
- Retry transient sharing and lock violations during checksum reads, installer copying and folder swaps, with bounded delays and cancellation support.
- Keep locked-path details in installer errors and save a diagnostic log without letting cleanup or logging failures obscure the original error.
- Add update regression coverage for inherited and nested working directories, preserved files and rollback copies, temporary locks, cancellation and persistent-lock diagnostics.

## 2.1.7

- Reveal the last added clothing item once per batch, including resource imports and gender copies, without scrolling through intermediate additions.
- Expand the target clothing category and clear a hiding search/filter when necessary to reveal the addition.
- Use compact clothing action buttons that wrap within narrow panes instead of clipping the Remove button.
- Replace the pants icon with a clearer two-leg outline, waistband and pockets.

## 2.1.6

- Highlight newly added clothing rows in light yellow for two seconds, then fade to normal over 0.7 seconds, including new imports and gender copies.
- Track each addition's age through selection and list refreshes; no click is required to clear the highlight.

## 2.1.5

- Keep male items first and female items below them within each clothing category, without separate gender dropdowns.
- Append added models and gender copies at the next position for their gender and clothing type, starting at zero for an empty type.
- Append imported resource items after existing positions while preserving source filename matching for YTD textures.
- Preserve the positions of existing items when reimporting their source models.

## 2.1.4

- Make horizontal model rotation follow the pointer and save an invert-panning option in Settings and the preview menu.
- Open texture rows by double-clicking and pass the selected variation into the separate 3D preview instead of resetting to A.
- Synchronize the texture list and preview selector, retaining the camera when changing variations.
- Import YTD files into existing drawables, including files selected separately from their models; report unmatched or ambiguous files.
- Separate the library into collapsible Male and Female groups, each with its own clothing slots.
- Keep preview tabs in their declared positions and remove button press scaling.
- Draw collapse arrows with light strokes on transparent charcoal headers.
- Add editor regression checks and optional real-model YTD material verification.

## 2.1.3

- Use `.duckproj` for new saves, exported workspaces and automatic recovery snapshots.
- Keep `.weftproj` and `.duck` projects readable; saving a legacy project offers a separate `.duckproj` copy.
- Preserve Durty Cloth `.dctproj` imports through adjacent built FiveM resources, without overwriting the original project.
- Retain legacy recovery assets and apply backup retention across all native project extensions.
- Register Open With for current, legacy and Durty Cloth project files while preserving other apps' associations.
- Update the bundled renderer to rustls 0.23.46 for RUSTSEC-2026-0346 through RUSTSEC-2026-0349.
- Compare decoded signing keys so Windows PEM line endings do not block release packaging.
- Add regression coverage for migration, portable exports, mixed backup formats and installer associations.

## 2.1.2

- Made repository and release documentation match public distribution.
- Prevented imported projects from selecting executable decoder DLLs.
- Rejected linked installer/uninstaller paths and validated all removal paths before deleting files.
- Required HTTPS throughout update redirects and bounded local update manifests.
- Restricted image decoders, blocked external delegates and bounded image resources.
- Enabled the public signed GitHub update feed for new installations and portable copies.
- Patched the renderer's zstd dependency to address RUSTSEC-2026-0344.
- Updated the bundled .NET and Windows Desktop runtimes to 10.0.13.
- Added security regression tests, secret scanning, dependency checks and private reporting guidance.

## 2.1.1

- Removed em dashes from application labels, renderer text and source documentation. Numeric ranges use ordinary hyphens.
- Added repository setup, build and test instructions, release documentation, contributor guidance and Windows CI.
- Packaged the installer and portable application for GitHub distribution.

## 2.1.0

- Added undo/redo, UV inspection, checker textures and clickable surface selection.
- Added saved outfit presets and procedural fitting poses based on model skin weights.
- Added deeper build validation, batch editing and naturally ordered image-folder imports.
- Added a FiveM companion for in-game clothing, outfits, tattoos and animation testing.
- Added transparent product screenshots, multiple views and contact sheets.
- Added revision comparison and conservative model LOD generation.

## 2.0.0

- Rebranded the editor to Duck Studio with the supplied duck logo.
- Fixed 3D preview block-color artifacts and opaque clothing visibility.
- Added texture hover previews, photo and PSD/PSB imports, and linked image refresh.
- Added tattoos, project history, texture optimization and automatic recovery backups.
- Added alt:V, single-player DLC and raw exports alongside FiveM.
- Added an installer, signed automatic update checks and install confirmation.
