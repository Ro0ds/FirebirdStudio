# Firebird Studio Roadmap

This roadmap records the agreed direction for Firebird Studio: a focused, cross-platform Firebird development and administration environment. Work should remain incremental, preserve existing behavior, and be validated against supported Firebird versions before release.

## Phase 1 — Reliability and quality of life

- [ ] Firebird 2.5, 3, 4 and 5 compatibility testing (deferred to the next release).
- [x] Persistent SQL editor workspace across normal restarts and crash recovery, including tab rename and close.
- [x] Connection diagnostics.
- [x] Better error reporting and actionable diagnostic details.
- [ ] Performance testing with large databases (deferred to the next release).
- [x] Keep connection-time metadata page retrieval off the UI thread so large catalogs do not freeze the window.

### Release scope: current Phase 1 build

The current release focuses on:

- Better error reporting, including immediate on-screen error feedback, automatic navigation to Messages, and detailed exception information.
- A timestamped Messages history, newest first, with distinct soft green success, yellow information, and red error entries.
- Persistent SQL editor workspace saves after typing pauses and on `Ctrl+S`, with footer confirmations. The workspace is retained on normal exit and restored on the next launch.
- All open editor tabs, titles, contents and active-tab selection are restored. Unexpected shutdowns are covered by the same durable workspace file.
- Editor tabs can be renamed or closed from their context menu; closing a tab removes its contents from the saved workspace. At least one blank editor remains available.
- The new-editor (+) tab stays immediately after the SQL editor tabs as tabs are added or closed.
- Ctrl+Click on a recognized database identifier in SQL Editor opens its table, view, procedure, trigger, or index inspector; an unambiguous field reference opens its owning table or view.
- Double-clicking supported objects in Database Objects opens their inspector. Double-clicking a constraint/key navigates to its owning table.
- Successful SQL execution switches from Messages to Results when Messages is currently selected.
- Connection diagnostics that distinguish host resolution, TCP reachability and the Firebird provider/authentication stage.

Firebird-version compatibility testing and large-database performance testing are intentionally deferred to the next release.

## Phase 2 — SQL development

- Execution plan viewer.
- [x] SQL parameters (named parameters with typed values and safe provider binding).
- [ ] SQL formatter (under revision: validate DML, DDL, procedural SQL, token preservation and spacing across Firebird 2.5/3/4/5 before marking complete).
- Query bookmarks and reusable snippets.
- Improved result grid and BLOB viewer.

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

## Product direction

Prioritize Firebird-specific depth, reliable support for older databases, fast metadata navigation, SQL development, and dependable administration. Avoid adding complexity or broad features without a clear benefit to Firebird users.


## Next-version suggestions

- Add a context menu to saved database connections with Create connection, Edit connection, Connect, Disconnect and Delete actions. Enable or disable actions according to the selected connection's state.
