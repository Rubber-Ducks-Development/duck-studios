# Releases and automatic updates

## Release key

The public RSA key is pinned in `Editor/Assets/UpdatePublicKey.pem`. Keep its matching private key in a secure location outside the repository. Never commit or attach the private key to a release. Existing installed copies require manifests signed by the pinned key.

## Build a release

1. Update the versions in `Editor/WeftStudio.csproj` and `Installer/DuckStudio.Setup.csproj`, the README download filename, and `CHANGELOG.md`.
2. Run the tests with `dotnet run --project Tests/DuckStudio.Tests.csproj -c Release`.
3. Build from PowerShell 7 with the .NET 10 SDK:

```powershell
./Build-Release.ps1 -SigningKeyPath 'D:/secure/duck-studio.private.pem' -ReleaseNotes 'What changed'
```

The output contains a self-contained Windows x64 application, `Installer/DuckStudioSetup.exe`, and a signed `UpdateFeed/latest.json`. Archive the application folder for portable distribution. Keep installer and portable archives in GitHub Releases rather than Git history.

## Public GitHub distribution

The repository and release downloads are public. Upload the installer, portable ZIP, signed latest.json and SHA256SUMS.txt to a versioned release targeting the tested commit. Mark the stable release as latest. Run source tests, Security-Checks.ps1, NuGet Audit including transitive packages and RustSec against Renderer/Cargo.lock before publication.

The renderer vendors rpf-archive to upgrade ruzstd to patched 0.9.1. Rebuild with cargo build --locked --release --manifest-path Renderer/Cargo.toml and copy the EXE to Vendor/Runtime/rage.exe. Preserve its license/resources and patch notes. See SECURITY.md for review limits and outstanding dependency maintenance warnings.

## Online feed publishing

Build into a fresh folder on Windows with PowerShell 7 and the .NET 10 SDK. Use the actual release version in InstallerUrl:

```powershell
./Build-Release.ps1 -SigningKeyPath 'D:/secure/duck-studio.private.pem' `
  -InstallerUrl 'https://github.com/Rubber-Ducks-Development/duck-studios/releases/download/v2.1.2/DuckStudioSetup.exe' `
  -FeedUrl 'https://github.com/Rubber-Ducks-Development/duck-studios/releases/latest/download/latest.json' `
  -ReleaseNotes 'What changed'
```

Upload the installer and signed manifest to those locations. Run Gitleaks on the staged package and verify the installed files, uploaded asset sizes and SHA256 before publishing. Exclude private keys, user files, PDBs and machine-specific paths. Never expose signing keys to pull-request workflows.

New copies use the public feed without authentication. For 2.1.1 or earlier copies, select the public feed in Help > Update settings or install the new release manually. Their existing local feeds do not change when the repository becomes public. Custom feed settings take precedence.

HTTP downloads and HTTPS redirects to HTTP or local files are rejected. Local signed feeds have the same manifest-size limit as HTTPS feeds.

## Update behavior

Checks run on startup and every six hours. A newer version asks whether to install. Not now skips automatic offers for that version; a manual check can offer it again. The app verifies the RSA-PSS signature, signed file size and SHA256 before installation, offers to save unsaved work, completes recovery, and closes.

The installer waits for the old process, verifies package files, and replaces an identified Duck Studio folder. Canceling close cancels installation. Uninstall preserves projects, history and backups.

The EXEs do not carry an Authenticode publisher certificate. If adding publisher signing later, sign the installer before hashing it and creating the signed release manifest.
