# Windows release packaging

This repository uses the root **VERSION** file as the single release-version source. The same value is imported by MSBuild through **Directory.Build.props** and read by the packaging script. Update **VERSION** once when preparing a release; use a SemVer value without a leading `v`.

## Requirements

- Windows x64
- .NET 10 SDK (the repository's global.json selects the 10.0.100 feature band)
- PowerShell 7 or Windows PowerShell 5.1 (for the packaging script only)

## Build release packages

Run from the repository root:

```powershell
.\scripts\publish-windows.ps1
```

The script runs the solution test suite first. To skip tests intentionally:

```powershell
.\scripts\publish-windows.ps1 -SkipTests
```

Two portable ZIP packages are produced:

| Package | Deployment | Runtime |
|---|---|---|
| FirebirdStudio-<version>-win-x64-framework-dependent.zip | Single-file executable | Requires the .NET 10 Desktop Runtime |
| FirebirdStudio-<version>-win-x64-self-contained.zip | Folder deployment | Includes the .NET runtime |

The self-contained package is deliberately published as a folder and then zipped. It is not bundled into a self-extracting single executable.

Both packages include the separate .NET updater application under `Updater/`. The desktop publish target builds and publishes the updater for the same runtime identifier and deployment mode as Firebird Studio. The updater is copied to the writable update cache before launch so it can safely replace its installed copy.

## Generated files

The script writes its generated outputs under `releases/`:

- The two versioned ZIP packages.
- `checksums.txt`, containing SHA-256 hashes for both ZIP files.
- `release-manifest.json`, describing the version, stable channel, target OS/architecture, deployment mode, runtime requirement, file size, and SHA-256 for each package.
- Temporary publish directories under `releases/.staging/`.

Existing files elsewhere under `releases/` are preserved. The two ZIP files for the current version and the staging directories owned by this script are replaced on each run.

The manifest does not include its own checksum because that would be self-referential.

When creating a GitHub Release, attach both ZIP packages, `release-manifest.json`, and `checksums.txt` from this directory. The in-app downloader requires the manifest and matching ZIP package to be release assets; the checksum text file is provided for manual verification. A checksum downloaded from the same release location detects accidental corruption but does not independently authenticate the publisher; release signing is a separate future task.

## Automated GitHub publishing

The repository includes a manually triggered GitHub Actions workflow at `.github/workflows/publish-windows-release.yml`.

To publish:

1. Update the root `VERSION` file to a new SemVer version that has not already been tagged.
2. Commit and push that version change to `main`.
3. Open the repository's **Actions** tab and select **Publish Windows release**.
4. Select **Run workflow** on the `main` branch.

The workflow runs on a Windows GitHub-hosted runner, executes the existing packaging script (including the solution tests), and creates a GitHub Release with both ZIP packages, the manifest, and checksums. It refuses to publish if the version's Git tag already exists. It does not change `VERSION`, and it never runs automatically on push or pull-request events.

This first automation covers **Windows x64 only**, matching the existing packaging script. Linux and macOS release packages are not produced by this workflow; those targets need their own packaging and validation before they can be published.
