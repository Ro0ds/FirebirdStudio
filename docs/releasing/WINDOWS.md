# Windows release packaging

This repository uses the root **VERSION** file as the single release-version source. The same value is imported by MSBuild through **Directory.Build.props** and read by the packaging script. Update **VERSION** once when preparing a release; use a SemVer value without a leading `v`.

## Requirements

- Windows x64
- .NET 10 SDK (the repository's global.json selects the 10.0.100 feature band)
- PowerShell 7 or Windows PowerShell 5.1

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

The self-contained package is deliberately published as a folder and then zipped. It is not bundled into a self-extracting single executable, avoiding the size and extraction behavior of that deployment mode.

## Generated files

The script writes its generated outputs under releases/:

- The two versioned ZIP packages.
- checksums.txt, containing SHA-256 hashes for both ZIP files.
- release-manifest.json, describing the version, stable channel, target OS/architecture, deployment mode, runtime requirement, file size, and SHA-256 for each package.
- Temporary publish directories under releases/.staging/.

Existing files elsewhere under releases/ are preserved. The two ZIP files for the current version and the staging directories owned by this script are replaced on each run.

The manifest does not include its own checksum because that would be self-referential. The checksum file covers the distributable ZIP packages only. A checksum downloaded from the same release location detects accidental corruption but does not independently authenticate the publisher; release signing is a separate future task.

This script creates local artifacts only. It does not create a GitHub Release, upload files, configure GitHub Actions, or implement the in-app updater.
