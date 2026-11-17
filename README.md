# Firebird Studio

A cross-platform, open-source Firebird database IDE foundation for Windows, Linux, and macOS.

> **Project status: functional early-development release.** Connection management, metadata exploration, SQL editing/execution, query history, editor recovery, and staged connection diagnostics are implemented. It is not yet a complete IBExpert replacement.

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
- Register remote TCP/IP and embedded connection profiles, including a local alias, server/port, database path or server-side alias, user, role, character set, SQL dialect, and client library path for embedded mode.
- Configure provider-level packet size, fetch size, database cache pages, pooling limits/lifetime, compression, database-trigger suppression, and garbage-collection behavior.
- Validate connection mode requirements and advanced option ranges, including the provider restriction against combining trigger suppression with pooling.
- Persist non-secret profile metadata as JSON in the current user's application-data directory.
- Write profile files atomically and restrict Unix directory/file permissions to the current user.
- Diagnose Firebird connections in stages: profile validation, DNS resolution, TCP reachability, and provider handshake/authentication, with elapsed times and detailed failure information.
- Connect and disconnect long-lived Firebird sessions from the desktop connection list.
- Automatically switch to Messages when an application/database error occurs, show an immediate footer notification, and retain detailed exception information, including stack traces and inner exceptions.
- Maintain a timestamped Messages history, newest first, with soft green success, yellow information, and red error entries.
- Autosave open SQL editor tabs 700 ms after typing stops; `Ctrl+S` saves immediately and displays a footer confirmation.
- Persist all open SQL editor tabs, titles, and query contents between application sessions; autosave after typing pauses, support Ctrl+S, recover after unexpected shutdowns, and notify the user when saved tabs are restored. Tabs can be renamed or closed from their context menu; closing a tab removes it from the saved workspace.
- Switch from Messages to Results after successful SQL execution when Messages is currently selected.
- Keep active provider connections owned by an application-level session manager, with duplicate-session prevention and deterministic asynchronous disposal.
- Display active session count and server version; close active sessions when the main window closes.
- Disable provider connection pooling for connection tests so a test establishes a fresh physical connection; apply saved pool settings to active sessions.
- Keep passwords out of the profile model and JSON file. Optionally remember passwords through Windows Credential Manager, macOS Keychain, or Linux Secret Service; passwords remain manually enterable when that facility is unavailable.

## Explicitly not implemented yet

- Native credential-store integration is provisional and still requires platform-specific integration tests.
- IBExpert-specific UI/metadata options (object-name capitalization, metadata cache, SQL plan display, startup auto-connect, idle disconnect, backup defaults, and pre/post-connect scripts); these belong to later feature areas, not the provider's connection string.
- Legacy NetBEUI/SPX transports and trusted authentication; neither is exposed until support is verified for the cross-platform provider targets.
- Multi-statement SQL scripts, parameter binding UI, editable result grids, data editing, schema/data comparison, backup/restore, monitoring, and import/export.
- Real-server integration tests across supported Firebird versions and OS-specific release packaging.

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

Avalonia supports Windows, macOS, and Linux, but support tiers vary by OS release. The application has not yet been built or smoke-tested on all three platforms. Linux may require X11 dependencies; native Wayland is an optional backend in Avalonia 12.1.x. See `docs/architecture/PLATFORM-SUPPORT.md`.

## Security notes

- Connection profiles contain no password property.
- Never add passwords to JSON, logs, exception telemetry, or command-line arguments.
- The connection prompt can load a remembered password from the OS credential store and lets the user remember or forget it after a successful connection. Connection tests still ask for a password and clear the field afterwards.
- The Firebird provider receives a connection string containing the password in process memory; this is currently unavoidable with the provider API used here. Clearing the UI field does not guarantee zeroization of immutable managed strings.
- If the OS credential facility is unavailable, password saving fails closed; manual one-session connection remains available without writing a plaintext fallback.
- The persistent SQL editor workspace writes open editor tabs to local application data 700 ms after typing stops or immediately on `Ctrl+S`. It is retained across normal application exits and unexpected shutdowns. Like query history, it contains SQL text; avoid embedding secrets in SQL statements.
- SQL execution is now available for one statement at a time. Each execution uses a transaction that is committed on success and rolled back on failure when possible. Result display is capped at 500 rows and approximately 2 million characters; BLOB/ARRAY values are represented by placeholders. Query results are displayed in a read-only, dynamically generated TableView. Query history stores the exact submitted SQL locally (including any literal values in it), retains the latest 250 executions, and restores a selected entry into the editor on double-click. It can be docked on the right or moved to a tab beside the SQL editor. Press F5 to execute the selected SQL, or the current line when no selection exists. Avoid embedding secrets in SQL text.
- Domain metadata is fetched in pages of 200 objects. Next/previous page entries replace the currently displayed page, keeping the tree bounded instead of accumulating all domain objects.

## License

Original Firebird Studio code in this repository is licensed under MIT. See `LICENSE`.
Third-party packages and tools are not relicensed by this project. See `docs/licensing/DEPENDENCIES.md` and preserve their required notices.


## Automated tests

A test project is included at `tests/FirebirdStudio.Tests`. It covers connection-profile validation, profile JSON persistence and fail-closed loading, provider connection-string mapping, session lifecycle using a fake provider connection, and the credential adapter using an in-memory fake `ILatchkey`. The fake vault is test-only and is never used by the application.

Run the suite from the solution root:

```powershell
dotnet test
```

The suite does not connect to a Firebird server or write to a user database. Native credential-store round-trip tests on Windows, macOS, and Linux remain separate platform integration tests.


### Initial read-only metadata explorer

The desktop client can read user tables, views, procedures, triggers, indexes, and domains from Firebird system catalogs on an active session. Domain inventory is paged in batches of 200 to avoid rendering tens of thousands of tree nodes at once. The inventory is displayed as an expandable tree: categories expand to objects, tables expand to columns and constraints, key constraints expand to key columns, indexes expand to ordered segments, and procedures expand to input/output parameters and source. Foreign keys show their referenced table/column mapping and referential actions. Top-level category order and system/light/dark theme preference are persisted locally; moving a category leaves its child objects attached. The desktop shell uses a standard File, Connection, Query, and View menu, with compact typography and a resizable query-history pane that can move to an editor tab. The metadata pane can also be resized. Catalog reads are asynchronous and cancellation-aware. This is not a full metadata editor or a verified compatibility guarantee; the catalog queries still require integration testing against Firebird 2.5, 3, 4, and 5. Packaged procedures are not yet disambiguated. Persistent query history stores submitted statements and supports double-click restore. SQL execution currently supports one statement per execution, commits a successful statement, rolls back on execution failure where possible, and displays up to 500 result rows in a read-only grid. Selecting a table opens a dedicated tab with its field metadata, a first-100-row data preview, constraints, and a generated DDL draft. The DDL draft is intentionally marked for review: domains, defaults, computed fields, triggers, and all check-constraint definitions are not yet fully reconstructed. Multi-statement scripts, parameter binding, and editable result grids remain future work. SQL text is stored exactly as submitted in the local query-history file; avoid putting secrets in SQL literals.
