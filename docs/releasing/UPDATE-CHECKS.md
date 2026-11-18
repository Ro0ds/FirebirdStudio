# In-app update checks and package downloads

Firebird Studio checks the GitHub Releases stable channel for a newer version. The running application displays its current version in the window title and status bar; both values come from the assembly version generated from the root `VERSION` file.

## Release discovery

- The application performs an update check after its normal startup loading has completed. The check runs asynchronously and does not block the UI.
- Further automatic checks run every 24 hours while the application remains open.
- Users can trigger a check from **Help → Check for updates…**.
- Only the latest published stable GitHub Release is considered. Draft and prerelease releases are not offered.
- When a newer version is found, the application displays its version and release notes. **View release** opens the release page in the system browser; **Later** dismisses the notification.

## Download and verification

- **Download update** downloads the release manifest and selects exactly one artifact matching the current operating system, process architecture, and deployment mode.
- The downloader currently recognizes Windows, Linux, and macOS identifiers, but a platform can download only when the release manifest actually contains a matching package. The published Windows packages are x64.
- The current deployment mode is detected from the presence of the bundled .NET host runtime. Framework-dependent and self-contained installations therefore select different ZIP packages.
- The package and manifest must be attached to the same GitHub Release. The required release assets are `release-manifest.json` and the package ZIP files named by that manifest. `checksums.txt` is also published for manual verification.
- Downloads are staged under the user's local application data directory:
  `%LOCALAPPDATA%/FirebirdStudio/Updates/<version>/` on Windows (the platform's LocalApplicationData directory on other operating systems).
- The downloader checks the manifest schema, product, stable channel, version, platform, architecture, deployment mode, asset name, declared size, and SHA-256 value.
- The ZIP is written to a temporary `.download` file. It is moved to its final name only after its size and SHA-256 match the manifest.
- A previously downloaded package is reused only after its size and SHA-256 are checked again. Canceled or failed downloads remove their partial file; if the process is forcibly terminated, the next attempt overwrites any leftover `.download` file before validating the result.
- After verification, the user can open the download folder and inspect or extract the ZIP manually.

## Important security boundary

SHA-256 verification detects corruption or a mismatch between the package and the published manifest. Since both are downloaded from the same GitHub Release, this does **not** independently authenticate the publisher or protect against an attacker who can replace both assets. Signed release metadata or another trusted signing mechanism remains a separate security milestone.

## Current limitations

This phase downloads and verifies packages only. It does **not** extract, install, replace application files, restart the application, or modify user data, settings, credentials, or database files. The user remains responsible for inspecting and extracting the downloaded ZIP. The external updater and rollback remain future work.

The check uses the GitHub API endpoint:

`https://api.github.com/repos/Ro0ds/FirebirdStudio/releases/latest`

If no stable release exists yet, GitHub returns not found; automatic checks remain quiet and a manual check reports that no stable release has been published.
