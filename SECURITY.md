# Security

## Supported version

Use the latest 2.1.x release, currently 2.1.2. Earlier versions should be updated before opening projects supplied by other people. Security fixes are released through this repository's Releases page and signed update feed.

## Report a vulnerability privately

Use [Report a vulnerability](https://github.com/Rubber-Ducks-Development/duck-studios/security/advisories/new). Include the affected version, reproduction steps and a minimal example. Do not post credentials, signing keys or exploit details in public issues. Test against your own files and installation.

## Release and repository protections

- The release-signing private key stays outside the repository and downloadable packages. The embedded public key verifies RSA-PSS/SHA256 manifests; it cannot sign an update.
- The updater verifies signed installer size and SHA256 before offering installation. HTTP downloads and HTTPS-to-HTTP redirects are rejected. Installation needs user confirmation.
- Imported project files cannot select executable CodeWalker DLLs. An absent bundled decoder must be selected locally.
- Image imports use explicit photo decoders with an allowlist of modules. External delegates, filters and script/vector coders are blocked; source size and pixel-cache resources are bounded.
- Installer paths reject traversal, alternate data streams and linked folders/files. Uninstall validates all owned paths before removal. Installation is per user.
- CI has read-only repository permissions except the CodeQL job's security-report upload permission. Actions use immutable commit IDs and checkout does not retain credentials. Fork pull requests do not receive release signing keys.
- GitHub secret scanning, push protection, dependency security updates and private vulnerability reporting are enabled. Dependency updates require review and passing checks.

## Checks and limits

Before publishing, run the source and security regression tests, Gitleaks against Git history and package contents, NuGet Audit including transitive dependencies, and RustSec against the renderer lockfile. Compare every release asset with `SHA256SUMS.txt`.

The publication review found no exposed credentials in Git history or the release scan. The signing key was not published. This is a targeted review, not a penetration test or a guarantee that every input parser is free of bugs. Native image, model and archive parsers remain an attack surface; use trusted assets and keep the app updated.

RustSec still reports maintenance warnings for the upstream `json` and `paste` crates. These are recorded as dependency maintenance risks, not treated as proof of safety. The confirmed ruzstd advisory was fixed by upgrading to 0.9.1.

EXEs currently have no Authenticode publisher certificate. Update-feed signatures protect the updater; they do not replace Windows publisher signing or verification of the first downloaded installer.
