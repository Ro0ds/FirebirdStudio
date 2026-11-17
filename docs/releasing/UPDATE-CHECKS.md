# In-app update checks

Firebird Studio checks the GitHub Releases stable channel for a newer version.

## Behavior

- The application performs an update check after its normal startup loading has completed. The check runs asynchronously and does not block the UI.
- Further automatic checks run every 24 hours while the application remains open.
- Users can trigger a check from **Help → Check for updates…**.
- Only the latest published stable GitHub Release is considered. Draft and prerelease releases are not offered.
- When a newer version is found, the application displays its version and release notes. **View release** opens the release page in the system browser; **Later** dismisses the notification.
- If a manual check finds no newer version, the application reports that it is up to date.

## Current limitations

This checkpoint only discovers releases and notifies users. It does not download packages, verify package checksums, install updates, restart the application, or modify application files. Users must review and install a release manually from GitHub.

The check uses the GitHub API endpoint:

`https://api.github.com/repos/Ro0ds/FirebirdStudio/releases/latest`

If no stable release exists yet, GitHub returns not found; automatic checks remain quiet and a manual check reports that no stable release has been published.
