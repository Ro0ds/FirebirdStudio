# Firebird Studio Roadmap

This roadmap tracks Firebird Studio as a cross-platform Firebird development and administration environment. Changes are delivered through PRs to `development` and validated against the relevant Firebird versions.

## Current stable release

**v0.2.0** ships the desktop redesign for Windows x64. It includes the new workspace and icon, configurable light and dark colors, connection and object navigation, redesigned history and output, and interface localization. The result grid supports column sorting, first and last row navigation, and row copying. Saved connections have a context menu with create, edit, connect, disconnect, and delete actions. [Release notes and validation](releasing/0.2.0.md)

## Phase 1 — Reliability and quality of life

- [ ] Automated integration tests against Firebird 2.5, 3, 4, and 5. The 0.2.0 candidate smoke test covered Firebird 5.0.4 embedded.
- [x] Persistent SQL editor workspace across normal restarts and crash recovery, including tab rename and close.
- [x] Connection diagnostics.
- [x] Better error reporting and actionable diagnostic details.
- [ ] Performance testing with large catalogs and query results.
- [x] Keep connection-time metadata page retrieval off the UI thread so large catalogs do not freeze the window.
- [ ] Integration tests for the native credential stores on Windows, Linux, and macOS.

### Next milestone

1. Add a repeatable Firebird 5 TCP/IP test against a disposable database. Cover connect and disconnect, metadata and DDL reads, typed SQL parameters, commit and rollback, and cancellation during query execution.
2. Run the same core checks against Firebird 4, 3, and 2.5. Record the engine version, connection mode, and any version-specific behavior in the [compatibility matrix](architecture/FIREBIRD-COMPATIBILITY.md).
3. Measure initial metadata loading, paging, search, and result display with a larger database. Record elapsed times, memory, and UI responsiveness so later changes can be compared against the same workload.

Windows x64 remains the packaged target. Linux and macOS build, runtime, and packaging validation are tracked in the [platform support policy](architecture/PLATFORM-SUPPORT.md).

### Reliability already delivered

- Better error reporting, including immediate on-screen error feedback, automatic navigation to Messages, and detailed exception information.
- A timestamped Messages history, newest first, with severity labels and icons, short summaries, and copyable details.
- Persistent SQL editor workspace saves after typing pauses and on `Ctrl+S`, with footer confirmations. The workspace is retained on normal exit and restored on the next launch.
- All open editor tabs, titles, contents and active-tab selection are restored. Unexpected shutdowns are covered by the same durable workspace file.
- Editor tabs can be renamed or closed from their context menu; closing a tab removes its contents from the saved workspace. At least one blank editor remains available.
- The new-editor (+) tab stays immediately after the SQL editor tabs as tabs are added or closed.
- Ctrl+Click on a recognized database identifier in SQL Editor opens its table, view, procedure, trigger, or index inspector; an unambiguous field reference opens its owning table or view.
- Double-clicking supported objects in Database Objects opens their inspector. Double-clicking a constraint/key navigates to its owning table.
- Successful SQL execution switches from Messages to Results when Messages is currently selected.
- Connection diagnostics that distinguish host resolution, TCP reachability and the Firebird provider/authentication stage.

## Phase 2 — SQL development

- Execution plan viewer.
- [x] SQL parameters (named parameters with typed values and safe provider binding).
- [ ] SQL formatter (under revision: validate DML, DDL, procedural SQL, token preservation and spacing across Firebird 2.5/3/4/5 before marking complete).
- Query bookmarks and reusable snippets.
- BLOB viewer and further result grid improvements, including filtering and record editing.
- Multi-statement script execution and multiple result sets.

## Phase 3 — Database intelligence

- Schema comparison and migration SQL generation.
- Visual database diagrams.
- Database monitoring.
- Database statistics.

## Phase 4 — Administration

- Backup and restore integration.
- User management.
- Generator/sequence management.
- Database validation.
- Advanced transaction and lock monitoring.

## Desktop identity and usability

The [desktop redesign plan](design/DESKTOP-REDESIGN.md) records the themes, workspace layout, accessibility checks, and milestones that led to v0.2.0. The [development and release workflow](releasing/DEVELOPMENT.md) describes how changes move from `development` to `main`.

The desktop app defaults to English and uses Brazilian Portuguese for strings missing from the English resource file. Choose **English** or **Português (Brasil)** in **File → Settings → Language**, save, and restart Firebird Studio to apply the change.

## Product direction

Prioritize Firebird-specific depth, reliable support for older databases, fast metadata navigation, SQL development, and dependable administration. Avoid adding complexity or broad features without a clear benefit to Firebird users.
