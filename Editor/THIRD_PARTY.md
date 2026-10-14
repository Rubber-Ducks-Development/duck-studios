# Third-party components

Image imports use Magick.NET-Q8-x64 and Magick.NET.Core 15.0.0, https://github.com/dlemstra/Magick.NET, under Apache License 2.0. The native library includes ImageMagick and its image format libraries. Their complete bundled notices and license texts are included in `third_party_licenses/Magick.NET-NOTICE.txt`; the managed library copyright notice is `third_party_licenses/Magick.NET-Core-Copyright.txt`. These components are unmodified.

Duck Studio bundles a modified `rage-cli` v0.26.0 executable for textured 3D PED and asset scene export. Upstream: https://github.com/VIRUXE/rage-cli, commit `bcefaa3e1aa95091964ab8cfa5c4908a2451a2d4`. Its source is released under the Unlicense; the complete modified source and original LICENSE are retained in the source folder under Renderer.

The modification adds `ped-scene` and `asset-scene` commands for orbitable 3D previews. It also adds `--override-slot`, `--override-ydd`, and `--override-ytd` to `rage screenshot --ped`. The app invokes this binary locally against the user's GTA V installation. It does not distribute GTA game data or recovered keys.

The renderer's Rust dependency names and licenses are listed in `third_party_licenses/DEPENDENCIES.csv` in the Windows distribution, alongside the license/notice files supplied by those packages.

The Windows package includes the user's local `CodeWalker.Core.dll` build with their stated redistribution permission, plus its SharpDX DLL dependencies. This is a separate component, not covered by Duck Studio's MIT license; its source is not included in the Duck Studio source archive. The maintainer should verify the exact permission grant before public redistribution. SharpDX is MIT licensed; its license notice is included in `third_party_licenses/SharpDX-LICENSE.txt`.

Duck Studio 2.1.2 vendors the published rpf-archive 0.10.0 library under Renderer/vendor/rpf-archive to require ruzstd 0.9.1 instead of vulnerable 0.8.3. Its Unlicense and required library resources are retained. This fixes RUSTSEC-2026-0344. See DUCK-STUDIO-PATCH.md for provenance.
