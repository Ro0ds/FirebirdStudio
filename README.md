# Firebird Studio

A cross-platform, open-source Firebird database IDE foundation for Windows, Linux, and macOS.

> **Project status: functional early-development release.** Connection management, metadata exploration, SQL editing/execution, query history, editor recovery, and staged connection diagnostics are implemented. It is not yet a complete IBExpert replacement.

The current stable release is **Firebird Studio 0.2.0**. It ships portable Windows x64 packages with and without the .NET runtime. [Download v0.2.0](https://github.com/Ro0ds/FirebirdStudio/releases/tag/v0.2.0) · [Release notes](docs/releasing/0.2.0.md) · [Product roadmap](docs/ROADMAP.md)

## Technology

- C# / .NET 10
- Avalonia UI 12.1.6
- AvaloniaEdit 12.0.0
- Firebird ADO.NET provider 10.3.4
- Latchkey 0.1.0 for native OS credential storage (provisional)
- MIT license for original project code
- Third-party dependencies retain their own licenses

## Implemented in this milestone

- Create, edit, list, and delete local connection profiles.
- Create a database on the Firebird instance behind the active connection and optionally save a profile for the new database.
- Register remote TCP/IP and embedded connection profiles, including a local alias, server/port, database path or server-side alias, user, role, character set, SQL dialect, and client library path for embedded mode.
- Configure provider-level packet size, fetch size, database cache pages, pooling limits/lifetime, compression, database-trigger suppression, and garbage-collection behavior.
- Validate connection mode requirements and advanced option ranges, including the provider restriction against combining trigger suppression with pooling.
- Persist non-secret profile metadata as JSON in the current user's application-data directory.
- Write profile files atomically and restrict Unix directory/file permissions to the current user.
- Diagnose Firebird connections in stages: profile validation, DNS resolution, TCP reachability, and provider handshake/authentication, with elapsed times and detailed failure information.
- Connect and disconnect long-lived Firebird sessions from the desktop connection list. Choose a detailed or minimal connection list and a light or dark theme. These preferences are saved locally.
- In **File → Settings**, customize the execution shortcut, pick accent and SQL selection colors, or choose English or Brazilian Portuguese. The language change applies after restart.
- Run SQL with typed named parameters. Format the selection or active document with `Ctrl+Shift+F`.
- Sort results by clicking a column heading, jump to the first or last row, and copy a row from its context menu.
- Automatically switch to Messages when an application/database error occurs, show an immediate footer notification, and retain detailed exception information, including stack traces and inner exceptions.
- Maintain a timestamped Messages history, newest first, with severity labels and icons, short summaries, and copyable details.
- Save open SQL tabs, titles, contents, and the active tab 700 ms after typing stops or immediately with `Ctrl+S`. The workspace is restored after normal exit or unexpected shutdown. Tabs can be renamed or closed from their context menu.
- Switch from Messages to Results after successful SQL execution when Messages is currently selected.
- Keep active provider connections owned by an application-level session manager, with duplicate-session prevention and deterministic asynchronous disposal.
- Display active session count and server version; close active sessions when the main window closes.
- Disable provider connection pooling for connection tests so a test establishes a fresh physical connection; apply saved pool settings to active sessions.
- Keep passwords out of the profile model and JSON file. Optionally remember passwords through Windows Credential Manager, macOS Keychain, or Linux Secret Service; passwords remain manually enterable when that facility is unavailable.

## Explicitly not implemented yet

- Native credential-store integration is provisional and still requires platform-specific integration tests.
- IBExpert-specific UI/metadata options (object-name capitalization, metadata cache, SQL plan display, startup auto-connect, idle disconnect, backup defaults, and pre/post-connect scripts); these belong to later feature areas, not the provider's connection string.
- Legacy NetBEUI/SPX transports and trusted authentication; neither is exposed until support is verified for the cross-platform provider targets.
- Multi-statement SQL scripts, editable result grids, data editing, schema/data comparison, backup/restore, monitoring, and import/export.
- Real-server integration tests across targeted Firebird versions, and release packages for Linux and macOS.

The `ISecureCredentialStore` abstraction is implemented by an Infrastructure adapter backed by Latchkey. The adapter uses only the native OS credential backend and does not configure a plaintext fallback. Latchkey is a provisional early-stage dependency until Windows, macOS, and Linux native integration tests pass.

## Requirements

- .NET 10 SDK
- Internet access for the first NuGet restore
- A reachable Firebird server/database to exercise connection testing

## Build and run

From the repository root:

```bash
dotnet restore FirebirdStudio.sln
dotnet build FirebirdStudio.sln --no-restore
dotnet run --project src/FirebirdStudio.Desktop/FirebirdStudio.Desktop.csproj
```

The connection profile editor can be used without a Firebird server. The connection test requires a real server and valid credentials.

## Platform notes

Windows x64 is the current packaged release target. Linux and macOS remain development targets. See the [platform support policy](docs/architecture/PLATFORM-SUPPORT.md) for the validation scope and the [Windows packaging guide](docs/releasing/WINDOWS.md) for release instructions.

## Security notes

- Connection profiles contain no password property.
- Never add passwords to JSON, logs, exception telemetry, or command-line arguments.
- The connection prompt can load a remembered password from the OS credential store and lets the user remember or forget it after a successful connection. Connection tests still ask for a password and clear the field afterwards.
- The Firebird provider receives a connection string containing the password in process memory; this is currently unavoidable with the provider API used here. Clearing the UI field does not guarantee zeroization of immutable managed strings.
- If the OS credential facility is unavailable, password saving fails closed; manual one-session connection remains available without writing a plaintext fallback.
- The persistent SQL editor workspace writes open editor tabs to local application data 700 ms after typing stops or immediately on `Ctrl+S`. It is retained across normal application exits and unexpected shutdowns. Like query history, it contains SQL text; avoid embedding secrets in SQL statements.
- SQL execution runs one statement at a time. Each execution uses a transaction that is committed on success and rolled back on failure when possible. Results are read-only and capped at 500 rows and approximately 2 million characters. BLOB and ARRAY values appear as placeholders. Query history stores the exact submitted SQL locally, including literal values, and retains the latest 250 executions. Double-click an entry to restore it. Avoid embedding secrets in SQL text.

Press `F9` by default to execute the selection or the single statement in the editor. If the document contains multiple statements, select one to run. A custom shortcut can be set in **File → Settings → Keyboard shortcuts**.

## License

Original Firebird Studio code in this repository is licensed under MIT. See `LICENSE`.
Third-party packages and tools are not relicensed by this project. See `docs/licensing/DEPENDENCIES.md` and preserve their required notices.


## Automated tests

A test project is included at `tests/FirebirdStudio.Tests`. It covers connection-profile validation, profile JSON persistence and fail-closed loading, provider connection-string mapping, session lifecycle using a fake provider connection, the credential adapter using an in-memory fake `ILatchkey`, SQL completion, formatter behavior, and DDL previews. The fake vault is test-only and is never used by the application.

Run the suite from the solution root:

```powershell
dotnet test
```

The suite does not connect to a Firebird server or write to a user database. Native credential-store round-trip tests on Windows, macOS, and Linux remain separate platform integration tests.


## Read-only metadata explorer

The explorer reads user tables, views, procedures, triggers, indexes, and domains from the active connection. It loads each category in batches of up to 200 objects and appends more as the user scrolls. The tree shows columns, constraints, ordered key/index segments, and procedure parameters and source where available. Category order is saved locally.

Table and view inspectors display field metadata and browse data in server-side pages of 200 rows. Tables also show constraints and a generated `CREATE TABLE` draft. View, trigger, and procedure inspectors format the corresponding source read from Firebird. The table draft covers the metadata available to the app and requires review before reuse in a migration.

See the [metadata explorer guide](docs/architecture/METADATA-EXPLORER.md) and [SQL workflow guide](docs/architecture/SQL-EXECUTION.md) for details. Real-server integration coverage for Firebird 2.5, 3, 4, and 5 is tracked in the [compatibility matrix](docs/architecture/FIREBIRD-COMPATIBILITY.md).
