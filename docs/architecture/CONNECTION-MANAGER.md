# Connection manager design

## Registration profile

`ConnectionProfile` stores non-secret registration settings. The profile name is the local database alias shown in the connection list. The database field accepts either a server-side database file path or a server-side alias.

### General settings

- Remote Firebird server over TCP/IP, or an embedded client/server library connection
- Host and TCP port for remote connections
- Database path or server-side alias
- User name and optional role
- Character set and SQL dialect
- Connection timeout
- Native client library path (required for embedded mode and not accepted for remote mode)

The Firebird .NET provider's `Default` server type is used for remote connections; `Embedded` is used for embedded connections. NetBEUI and SPX are not offered: they are legacy IBExpert protocol choices and are not part of this cross-platform provider workflow. A local server can still be reached over TCP/IP by using `localhost` as the host.

### Advanced provider settings

The editor exposes provider connection-string settings that are supported by the selected Firebird .NET provider:

- Packet size
- Fetch size
- Database cache pages
- Connection pooling, minimum/maximum pool size, and connection lifetime
- Wire compression
- Suppress database triggers
- Disable garbage collection

The provider does not permit connection pooling together with suppressed database triggers; profile validation rejects that combination. Suppressing database triggers can bypass database-side business rules and is off by default. Disabling garbage collection is also off by default.

A connection test always disables pooling so it tests a newly opened connection rather than reusing an existing pooled connection. Pool configuration is applied to active sessions; connection tests always bypass pooling.

### Deliberately not exposed as connection options

IBExpert includes options that belong to its own UI, metadata explorer, SQL editor, startup workflow, or backup/restore tools rather than the database attachment itself. Examples include capitalization of displayed object names, font character set, metadata cache, SQL plan display, performance analysis, auto-connect at startup, idle disconnect, backup defaults, and scripts before/after connect. These should be implemented in their corresponding Firebird Studio feature areas instead of being stored as inert connection fields.

The provider path also does not expose IBExpert's legacy NetBEUI/SPX transports or its proprietary additional-connect-parameter UI. Trusted authentication is not exposed until it can be verified across the supported provider and Firebird server versions.

## Password handling

A password is deliberately not a property of the profile model. Connection tests ask for a password and clear the input after the attempt. When connecting to a saved profile, the desktop optionally retrieves a remembered password from `ISecureCredentialStore`; the password prompt allows the user to replace it, keep it, or forget it after a successful connection. The password is never written to the JSON profile file. Removing a profile first commits the profile-file deletion and then attempts to remove its OS credential. If profile persistence fails, the credential is left intact. If the native vault deletion fails after the profile is removed, the UI warns that an orphaned vault entry may remain.

## Persistence

`JsonConnectionProfileRepository` stores profile metadata under the current user's application-data directory in `FirebirdStudio/connections.json`.

Writes use a temporary file in the same directory followed by a replace/move operation. On Unix-like systems, the application directory is restricted to owner read/write/execute and the profile file to owner read/write. Windows relies on the current user's application-data directory ACL inherited from the OS profile; explicit Windows ACL hardening remains to be reviewed.

The repository serializes operations within one process. It does not yet implement cross-process locking or concurrent multi-instance conflict resolution.

Malformed profile files fail with an error and are not silently overwritten. New profile properties have defaults so profiles created by earlier project builds remain readable when optional fields are absent.

## Connection testing

`FirebirdConnectionTester` first validates the profile. For remote profiles it then resolves the host and tests TCP reachability to the configured port before opening a fresh provider connection. For embedded profiles it verifies that the configured client library exists before attempting the provider connection. The report includes per-stage elapsed times, the resolved/reachable address where applicable, the Firebird server version on success, and sanitized exception details on failure. Connection pooling is disabled for this test.

The error result does not include the connection string, and the exact supplied password is redacted from provider error text if it appears there.

A connection test is not a persistent database session. The desktop session manager separately owns long-lived connections.

## Create a database

Open **File → Create database**. Choose **Use the active Firebird connection** to create with the current instance. The dialog displays its version. For TCP/IP, enter a path or alias on the server. For an embedded connection, choose an absolute local path.

The local file pickers for creating a database and registering a connection both accept `.fdb` and `.gdb`. When saving a new database through the picker, the default extension is `.fdb`.

Creation uses a separate provider operation with the selected credentials and rejects existing targets. The current connection stays open. If requested, the app saves a separate connection profile after creating the file.

On Windows x64, choose **Use embedded Firebird** and select a version to create a local database. This option is available with or without an active connection. The available Firebird releases are 2.5.9, 3.0.14, 4.0.7, and 5.0.4. A saved embedded profile records the chosen version. The connection editor also offers an external client library for existing installations.

In **File → Settings → Embedded Firebird**, **Download on demand** is the default and retrieves the engine when creating a database, testing its connection, or connecting. **Download all engines** installs the missing versions when Settings is saved. Downloaded engines are reused from the per-user cache under `%LOCALAPPDATA%\FirebirdStudio\engines`. Packages come from official Firebird releases and are checked against pinned sizes and SHA-256 digests before installation. [Firebird releases](https://github.com/FirebirdSQL/firebird/releases)

On Windows, engine downloads use the same TLS 1.2 configuration as application update downloads. The GitHub release downloads require HTTPS access to `github.com` and `release-assets.githubusercontent.com`.

Embedded connections use a separate provider load context for each native library so that version-specific capabilities can coexist. On Windows x64, the integration smoke test opened simultaneous connections to all four engines and verified the format of the created database files through `MON$DATABASE`. Firebird 2.5, 3.0, 4.0, and 5.0 reported ODS 11.2, 12.0, 13.0, and 13.1 respectively.

## Credential storage

`ISecureCredentialStore` is the application port for OS-backed secrets. `LatchkeySecureCredentialStore` implements it through Latchkey 0.1.0 and selects only the library's native `Auto` backend: Windows Credential Manager, macOS Keychain Services, or Linux freedesktop Secret Service (typically backed by GNOME Keyring or KWallet). The adapter does not configure Latchkey's plaintext file backend or any fallback.

Linux may have no Secret Service session in headless environments. When the native facility is unavailable, saving/retrieving credentials reports an error; the desktop still allows a manual one-session connection without persisting the password. Native integration tests on all supported OSes are still required. Latchkey 0.1.0 is an early-stage dependency and remains provisional pending those tests.

## Known gaps

- Native credential-store behavior has not yet been integration-tested on Windows, macOS, or Linux.
- No retry/backoff, connection health, or reconnect behavior exists yet.
- No cross-process profile file locking exists yet.
- Deterministic automated tests have been added for profile validation/persistence, provider connection-string mapping, credential adapter behavior with a fake `ILatchkey`, and session lifecycle with a fake provider connection. The [automated test guide](../../tests/README.md) describes this coverage and the remaining integration tests.
- Connection option compatibility with Firebird 2.5, 3, 4, and 5 still requires real-server integration tests.


## Active session lifecycle

The desktop can now open and close long-lived provider connections through `FirebirdSessionManager`. The manager owns provider connections behind an internal `IFirebirdProviderConnection` adapter, preventing provider types from leaking into Application or Core. It prevents a second active session for the same profile identifier, serializes connect/disconnect/dispose operations, and disposes active sessions when the main window closes. A failed explicit disconnect leaves the session visible so callers are not told it is closed when provider disposal failed. Connection tests use the same connection-string factory but force pooling off; active sessions honor the saved pooling options.

The session manager receives the password from the desktop prompt. The prompt can be prefilled from the OS credential store and offers an opt-in remember setting. The provider connection string necessarily holds the password in process memory while the connection is active; the OS credential store protects data at rest but does not protect against processes running as the same OS user.

The session abstraction exposes dedicated contracts for metadata reads, paged metadata reads, and SQL execution while keeping provider types out of Application and Core. It still does not expose a public raw provider connection. Unit tests do not connect to a real Firebird server; server-version-specific and native keyring integration tests remain required.
