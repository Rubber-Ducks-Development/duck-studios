<p align="center">
  <img src="Editor/Assets/AppIconSource.png" alt="Duck Studio logo" width="160">
</p>

# Duck Studio

Duck Studio is a Windows workspace for creating, previewing and exporting GTA V freemode clothing and tattoo packs. Maintained by Rubber Ducks Development.

**Current version: 2.1.2. Source and downloads are public.**

[Download the latest release](https://github.com/Rubber-Ducks-Development/duck-studios/releases/latest) · [Feature guide](Editor/FEATURES-2.1.md) · [Release notes](CHANGELOG.md)

## Install or run

1. Download `DuckStudioSetup.exe` from the latest release to install the app for your Windows account.
2. For a portable copy, download `DuckStudio-Windows-2.1.2.zip`, extract it, and run `DuckStudio.exe` inside the folder. Keep its runtime files alongside the EXE.
3. Link your own GTA V installation in Settings to preview clothing on a full character. Asset-only previews do not require GTA V.

The release includes the .NET desktop runtime. Downloads do not require a GitHub account. Use the assets on this repository's Releases page and the supplied `SHA256SUMS.txt` to verify downloads.

## Features

| Workflow | Included tools |
| --- | --- |
| Clothing and textures | YDD/YTD import, component and prop slots, PNG/JPG and other image imports, PSD/PSB support, linked source updates and hover texture previews |
| 3D inspection | Clothing and full PED previews, saved outfits, texture variations, skin-weight fitting poses, UV overlays, checker textures and surface selection |
| Tattoos | Artwork import, gender and body-zone controls, UV placement, PED previews and overlay/shop metadata export |
| Editing and recovery | Undo/redo, batch naming and imports, automatic backups, close confirmation, project history and revision comparison |
| Optimization | Texture sizing/compression/mipmaps, duplicate cleanup, geometry statistics, LOD inspection and conservative LOD generation |
| Build targets | FiveM, alt:V, single-player DLC archives and raw game assets |
| Testing and presentation | FiveM clothing/outfit/tattoo companion, validation reports, transparent product screenshots and contact sheets |
| Distribution | Per-user installer and signed update manifests with confirmation before installing an update |

## Build from source

Requirements: Windows x64 and the .NET 10 SDK. Publishing the installer also requires PowerShell 7. Rebuilding the renderer requires Rust and the Windows C++ build tools.

```powershell
git clone https://github.com/Rubber-Ducks-Development/duck-studios.git
cd duck-studios
dotnet build Editor/WeftStudio.csproj -c Release
dotnet run --project Tests/DuckStudio.Tests.csproj -c Release
```

`Vendor/Runtime` contains the CodeWalker, SharpDX and renderer binaries used by release packaging. To rebuild the renderer:

```powershell
cargo build --release --manifest-path Renderer/Cargo.toml
Copy-Item Renderer/target/release/rage.exe Vendor/Runtime/rage.exe
```

Create a distributable application and installer with the public-key-matching release key:

```powershell
./Build-Release.ps1 -SigningKeyPath 'D:/secure/duck-studio.private.pem' -ReleaseNotes 'Describe the release'
```

The private key is kept outside this repository. See [RELEASING.md](RELEASING.md) for packaging, signing and update-feed publishing.

## Source layout

```text
Editor/             WPF editor, image imports, previews and pack builder
Installer/          Installer, updater maintenance and uninstall handling
Renderer/           Modified rage-cli source
Tests/              Generated-fixture regression tests
Vendor/Runtime/     Dependencies used when packaging Windows releases
Vendor/Licenses/    Third-party license and notice files
.github/workflows/  Windows build and test checks
```

The tests generate fixtures under `work/DuckStudioTests` and do not need GTA V, private signing keys or game assets. Set `DUCK_STUDIO_TEST_ROOT` to choose another fixture directory.

## Preview and update details

Desktop fitting animations use procedural joint rotations and real skin weights. Desktop tattoo placement is a UV approximation. Use the FiveM companion to check actual game animation, decoration projection and clothing clipping. Live FXServer testing remains a separate integration check.

Conservative LOD generation protects boundaries, materials and skin weights. Reduction depends on the mesh; review each generated level before applying it.

New installations and portable copies check the public signed [GitHub update feed](https://github.com/Rubber-Ducks-Development/duck-studios/releases/latest/download/latest.json). The app asks before installing and verifies the pinned release signature, installer size and SHA256. HTTPS redirects cannot downgrade to HTTP. Custom signed local/shared feeds remain supported.

For copies installed before 2.1.2, set Help > Update settings to `https://github.com/Rubber-Ducks-Development/duck-studios/releases/latest/download/latest.json` or install the latest release manually. Existing custom feed settings take precedence.

Update to 2.1.2 before opening projects from other people. Imported projects cannot choose executable DLLs; a missing decoder must be selected locally. See [SECURITY.md](SECURITY.md) for private vulnerability reporting, security controls and review limits.

## License and dependencies

Duck Studio's editor and installer use the [MIT license](LICENSE). The modified renderer retains its [Unlicense](Renderer/LICENSE). Other bundled components keep their own licenses and notices; see [THIRD_PARTY.md](THIRD_PARTY.md).

Duck Studio uses assets from your own GTA V installation. GTA game data, recovered keys, user projects and release signing secrets are excluded from the repository.
