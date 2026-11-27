# Implementation and acceptance roadmap

The product-level four-phase roadmap is maintained in [`../ROADMAP.md`](../ROADMAP.md). This file tracks implementation slices and their acceptance evidence; it does not redefine the product phases.

The v0.2.0 desktop redesign is published for Windows x64. The [release notes](../releasing/0.2.0.md) record the candidate smoke test on Firebird 5.0.4 embedded. The core TCP/IP scenario passed on Windows x64 with Firebird 2.5, 3, 4, and 5. Fedora validation is pending. [Compatibility matrix](FIREBIRD-COMPATIBILITY.md)

| Phase | Scope | Acceptance evidence | Status |
|---|---|---|---|
| 0 | Repository foundation and desktop shell | Restore/build/run on all three OS families | Windows x64 packages published, Linux and macOS verification pending |
| 1A | Connection profiles and connection testing | Profile validation and persistence tests, integration tests on FB 2.5, 3, 4, 5 | Registration fields and deterministic tests implemented. Embedded FB 5.0.4 smoke and TCP/IP session open/close on all four versions passed. |
| 1B | Persistent secure credential storage | Native keyring integration tests on Windows, macOS, Linux; fail-closed behavior | Latchkey adapter and remember/forget UI implemented provisionally; automated adapter tests use a fake; native platform integration tests pending |
| 1C | Active connection/session lifecycle and reliability | Connect/disconnect, state transitions, detailed errors, SQL recovery, staged connection diagnostics, concurrency and disposal tests | Session lifecycle, diagnostic reporting, and editor recovery implemented. The FB 5.0.4 embedded smoke covered connection switching, queued cancellation, and restoration after restart. Broader integration pending. |
| 1D | Database creation on the active instance | Create, reopen, query, and reject an existing target | Passed with Firebird 5.0.4 over TCP/IP. [Creation workflow](CONNECTION-MANAGER.md#create-a-database) |
| 1E | Managed embedded engines | Validate both download settings and create and reopen with the selected engine | Passed on Windows x64 with Firebird 2.5.9, 3.0.14, 4.0.7, and 5.0.4. |
| 2 | Metadata explorer | Verified metadata fixtures per server version | Read-only catalog implemented. Table listing, search, columns, and table DDL passed over TCP/IP on all four versions. The FB 5.0.4 embedded smoke also covered paging and view and trigger DDL. |
| 3 | SQL editor and execution | Parameters, transactions, cancellation, errors, multi-statement behavior documented | Initial single-statement execution, transaction commit/rollback, cancellation, 500-row read-only TableView preview, configurable execution shortcut with `F9` as the default, table-inspection tab (fields/data/constraints/generated DDL), and persistent 250-entry query history with double-click restore and right/tab docking implemented. Typed named parameter binding and SQL formatting are available. Multi-statement scripts remain planned |
| 4 | Data grid and record editor | Paging, sorting, filtering, safe updates | Table data paging and read-only result sorting, row navigation, and copying shipped. Filtering and record editing remain planned. |
| 5 | Object editors | Round-trip DDL tests for tables, views, procedures, triggers, domains, indexes | Not implemented |
| 6 | Security administration | Roles, users, grants, privilege tests | Not implemented |
| 7 | Backup, restore, validation, statistics | Utility compatibility and failure recovery tests | Not implemented |
| 8 | Schema comparison | Deterministic diff tests across versions | Not implemented |
| 9 | Data comparison and synchronization | Preview, transaction strategy, conflict handling, rollback tests | Not implemented |
| 10 | Monitoring and diagnostics | Version-aware monitoring queries and permissions behavior | Not implemented |
| 11 | Import/export | Encoding, nulls, decimals, dates, large-file tests | Not implemented |
| 12 | Release hardening | OS packaging, SBOM, license notices, security and regression suite | Windows x64 packaging, PR validation, and stable release workflow are in use. Linux and macOS packaging and a complete SBOM are pending. |

No phase is complete merely because its UI exists. Each phase requires acceptance evidence.


## Read-only metadata explorer (initial milestone)

- [x] Read user tables (including global temporary tables) and views from `RDB$RELATIONS`.
- [x] Read user procedures from `RDB$PROCEDURES`.
- [x] Read user triggers from `RDB$TRIGGERS`.
- [x] Read user indexes from `RDB$INDICES`.
- [x] Read user domains from `RDB$FIELDS`.
- [x] Expose metadata through the active session only; no separate credential prompt.
- [x] Use asynchronous catalog reads and cancellation-aware provider calls.
- [x] Display discovered objects in an expandable category tree.
- [x] Expand tables to show columns and basic type/domain/nullability details.
- [x] Expand tables to show primary, unique, foreign-key, CHECK, and NOT NULL constraints.
- [x] Show foreign-key target tables/columns and ON UPDATE / ON DELETE rules.
- [x] Allow the metadata pane height to be resized.
- [x] Expand standalone index nodes to show ordered index segments and catalog selectivity.
- [x] Expand procedures to show ordered input/output parameters and procedure source.
- [x] Persist top-level metadata category ordering; moving a category keeps its children attached.
- [x] Persist light/dark theme selection.
- [x] Page domain metadata in batches of 200; request more objects on demand instead of rendering the full catalog at once.
- [ ] Add expression/source details for CHECK constraints and expression indexes.
- [ ] Add object-specific child nodes for triggers and views.
- [ ] Verify catalog queries against real Firebird 2.5, 3, 4, and 5 databases.

The explorer remains read-only. It does not execute user SQL or modify database metadata. Packaged procedure names are not yet disambiguated; additional object-specific navigation remains future work.


## Initial SQL execution and query history

- [x] Execute one SQL statement on the selected active connection.
- [x] Execute within an explicit transaction; commit after successful execution and attempt rollback on failure.
- [x] Support asynchronous execution cancellation.
- [x] Display the first 500 rows of the first result set and report when additional rows exist.
- [x] Persist the latest 250 submitted statements locally, including failed/cancelled attempts.
- [x] Restore a history entry into the SQL editor on double-click.
- [x] Bind typed named parameters through the Firebird provider.
- [ ] Add multi-statement script execution, multiple result sets, and a structured editable result grid.
- [ ] Verify provider transaction, cancellation, and result behavior against Firebird 2.5, 3, 4, and 5.

Query history stores SQL text exactly as submitted. It is local application data, not a credential store; users should avoid embedding secrets in SQL literals.
