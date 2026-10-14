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

## Private GitHub distribution

This repository is private. Its GitHub release assets require an account with repository access. Upload the installer, portable ZIP, release notes and checksums to a versioned release. Preserve repository visibility.

The desktop updater has no GitHub authentication flow. A private GitHub release URL cannot be used as an anonymous automatic update channel. Continue using the signed local/shared feed while the repository is private, or use a separately accessible HTTPS feed.

## Online feed publishing

When an accessible update host is available, pass exact URLs during packaging:

```powershell
./Build-Release.ps1 -SigningKeyPath 'D:/secure/duck-studio.private.pem' `
  -InstallerUrl 'https://your-host/DuckStudioSetup.exe' `
  -FeedUrl 'https://your-host/latest.json' `
  -ReleaseNotes 'What changed'
```

Upload the installer and signed manifest to those locations. For a public GitHub release, versioned release-asset URLs can be used. Configure the feed in Help > Update settings.

## Update behavior

Checks run on startup and every six hours. A newer version asks whether to install. Not now skips automatic offers for that version; a manual check can offer it again. The app verifies the RSA-PSS signature, signed file size and SHA256 before installation, offers to save unsaved work, completes recovery, and closes.

The installer waits for the old process, verifies package files, and replaces an identified Duck Studio folder. Canceling close cancels installation. Uninstall preserves projects, history and backups.

The EXEs do not carry an Authenticode publisher certificate. If adding publisher signing later, sign the installer before hashing it and creating the signed release manifest.
