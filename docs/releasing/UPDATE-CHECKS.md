# In-app update checks, downloads, and installation

Firebird Studio checks the GitHub Releases stable channel for a newer version. The running application displays its current version in the window title and in **Help → About Firebird Studio**. Both values come from the assembly version generated from the root `VERSION` file.

## Release discovery

- The application checks for updates after normal startup loading has completed. The check is asynchronous and does not block the UI.
- Further automatic checks run every 24 hours while the application remains open.
- Users can trigger a check from **Help → Check for updates…**.
- Only the latest published stable GitHub Release is considered. Draft and prerelease releases are not offered.
- When a newer version is found, the application displays its version and release notes. **View release** opens the release page in the system browser; **Later** dismisses the notification.

## Download and verification

- **Download update** downloads the release manifest and selects exactly one artifact matching the current operating system, process architecture, and deployment mode.
- The manifest must contain a package for the current platform and architecture. A platform cannot install until its corresponding release package is published.
- The current deployment mode is detected from the bundled .NET host runtime. Framework-dependent and self-contained installations therefore select different ZIP packages.
- The package and manifest must be attached to the same GitHub Release. Required assets are `release-manifest.json` and the package ZIP files named by that manifest. `checksums.txt` is also published for manual verification.
- Downloads are staged under the user's local application data directory: `<LocalApplicationData>/FirebirdStudio/Updates/<version>/`.
- The downloader checks the manifest schema, product, stable channel, version, platform, architecture, deployment mode, asset name, declared size, and SHA-256 value.
- The ZIP is written to a temporary `.download` file. It is moved to its final name only after its size and SHA-256 match the manifest.
- A cached package is reused only after its size and SHA-256 are checked again. Canceled or failed downloads remove their partial file.

## External updater application

The desktop application launches the separately published `FirebirdStudio.Updater` executable from a copy in the writable update cache. The updater is a C#/.NET application, not a shell script, and is packaged alongside Firebird Studio for the target runtime identifier.

- The updater runs as a separate process so it can replace the desktop application's files after the desktop process exits.
- Before exiting, Firebird Studio saves the SQL editor workspace, cancels background work, and closes active Firebird sessions.
- The updater waits for the parent process to exit before touching the installation directory.
- The updater independently rechecks the package byte length and SHA-256 before extracting anything.
- Archive paths are validated before extraction. Absolute paths, traversal segments, duplicate file paths, symbolic links, Windows reserved names, alternate data streams, database/backup extensions, and excessive archive expansion are rejected.
- Installation is an overlay: only files present in the release package are added or replaced. Files absent from the package are not deleted.
- Existing files are copied to a versioned backup directory before replacement. Each replacement is staged in the destination directory and moved into place. Unix executable permissions from ZIP entries are preserved on Linux and macOS. If a later operation fails, changed files are restored and newly added files are removed.
- If rollback succeeds, the previous application version is restarted. If rollback is incomplete, the updater leaves the backup and log in the update cache and does not restart automatically.
- The updater log is written to `updater.log` in the versioned update cache.
- The updater runs with the current user's permissions and does not request elevation. The installation directory must be writable by that user.
- Connection profiles, credentials, preferences, query history, SQL editor recovery data, and database files are outside the installation directory and are not part of the update operation.

The updater is a separate .NET application so the update workflow is not tied to PowerShell or a Windows-only shell. Platform-specific executable naming and filesystem behavior are selected by the .NET runtime. Automatic installation is available only in published packages that include the updater; development builds continue to use manual extraction. Obsolete application files absent from the new package are intentionally not deleted.

## Important security boundary

SHA-256 verification detects corruption or a mismatch between the package and the published manifest. Since both are downloaded from the same GitHub Release, this does **not** independently authenticate the publisher or protect against an attacker who can replace both assets. Signed release metadata or another trusted signing mechanism remains a separate security milestone.

## Current limitations

The updater is designed as a cross-platform .NET application, but automatic installation still requires a release package for the current platform and architecture. The existing publishing script currently generates Windows x64 packages only. Linux and macOS installation become available when matching packages are added to the release manifest and publishing workflow. Installations in protected locations may require the user to choose a writable installation directory; the updater deliberately does not request elevation.

The check uses the GitHub API endpoint:

`https://api.github.com/repos/Ro0ds/FirebirdStudio/releases/latest`

If no stable release exists yet, GitHub returns not found; automatic checks remain quiet and a manual check reports that no stable release has been published.
