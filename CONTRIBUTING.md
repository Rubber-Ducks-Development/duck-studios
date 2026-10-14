# Contributing

## Local checks

Use Windows x64 with the .NET 10 SDK. Build the editor and run generated-fixture tests:

```powershell
dotnet build Editor/WeftStudio.csproj -c Release
dotnet run --project Tests/DuckStudio.Tests.csproj -c Release
```

For renderer changes, build with Rust and run the relevant renderer tests. Full PED previews require a local GTA V installation. Validate exports on the target game/server before describing runtime behavior as verified.

## Changes

- Keep the existing compact WPF interface and keyboard behavior consistent.
- Use ordinary hyphens instead of em dashes in UI strings and documentation.
- Preserve source assets when implementing optimization, restoration or bulk edits.
- Include focused tests for changes to project state, file recovery and build output.
- Update the changelog and feature guide for user-visible changes.
- Keep generated artifacts, user projects, GTA files, credentials and private signing keys out of commits.

See `RELEASING.md` for release packaging. Keep each dependency's license and notice files intact.

Run `./Security-Checks.ps1` before pushing. For vulnerabilities, use the private reporting link in [SECURITY.md](SECURITY.md) instead of posting exploit details in a public issue. New dependencies and workflow changes must pass the security checks.
