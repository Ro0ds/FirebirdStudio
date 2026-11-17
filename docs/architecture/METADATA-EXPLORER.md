# Read-only metadata explorer

## Initial scope

The first explorer milestone reads the following user-defined object categories from the active Firebird session:

- Tables, including global temporary tables
- Views
- Stored procedures
- Triggers
- Indexes
- Domains

Metadata browsing uses `SELECT` statements against Firebird system catalog tables only. It does not execute user-provided SQL, DDL, DML, or administrative statements; user SQL execution is a separate SQL editor workflow documented in `SQL-EXECUTION.md`.

## Catalog sources

| Category | Catalog | Important filter/details |
|---|---|---|
| Tables | `RDB$RELATIONS` | `RDB$VIEW_BLR IS NULL`; `RDB$SYSTEM_FLAG` is zero or null |
| Views | `RDB$RELATIONS` | `RDB$VIEW_BLR IS NOT NULL`; `RDB$SYSTEM_FLAG` is zero or null |
| Procedures | `RDB$PROCEDURES` | `RDB$SYSTEM_FLAG` is zero or null; input/output parameter counts and source |
| Procedure parameters | `RDB$PROCEDURE_PARAMETERS` | Direction, position, domain, nullability and default source; joined to `RDB$FIELDS` for type details |
| Triggers | `RDB$TRIGGERS` | `RDB$SYSTEM_FLAG` is zero or null; relation, trigger type and inactive flag |
| Indexes | `RDB$INDICES` | `RDB$SYSTEM_FLAG` is zero or null; relation, segment count, uniqueness and inactive flag |
| Domains | `RDB$FIELDS` | `RDB$SYSTEM_FLAG` is zero or null; field type, byte length and scale |
| Table constraints | `RDB$RELATION_CONSTRAINTS` | Constraint type, relation and supporting index |
| Foreign-key details | `RDB$REF_CONSTRAINTS` | Referenced unique/primary constraint and update/delete rules |
| Key columns | `RDB$INDEX_SEGMENTS` | Ordered local and referenced key columns |

Table/view classification by `RDB$VIEW_BLR` and user-object filtering by `RDB$SYSTEM_FLAG` are documented in the Firebird 2.5 language reference. The selected catalog tables and columns are present in the Firebird 2.5 system-table reference. This documentation review is not a substitute for integration tests against each supported server release.

## Expandable metadata tree

- The desktop groups discovered objects under expandable categories.
- Expanding **Tables** reveals individual user tables; expanding a table reveals its columns and a nested **Constraints** group.
- Column details are read from `RDB$RELATION_FIELDS` joined to `RDB$FIELDS`: domain name, field type/subtype codes, byte length, character length, numeric precision, scale, character set, nullability, and ordinal position.
- Constraint details are read from `RDB$RELATION_CONSTRAINTS`, with foreign-key relationships and referential actions from `RDB$REF_CONSTRAINTS`.
- Primary-key, unique-key, and foreign-key key columns are read from `RDB$INDEX_SEGMENTS`. Foreign-key segments are paired by position with the referenced primary/unique index segments, allowing the tree to show local-to-referenced column mappings.
- Constraint nodes include type, supporting index (when present), referenced table (for foreign keys), and ON UPDATE / ON DELETE rules. CHECK and NOT NULL constraints are listed even though their expression/source is not yet displayed.
- Standalone index nodes expand to ordered segment children from `RDB$INDEX_SEGMENTS`, including each segment's column name, zero-based key position, and catalog selectivity. Indexes without segment rows (for example, expression indexes) currently have no segment children; their expression source is not yet displayed.
- Stored procedures expand to an input/output parameter group and a source node. Parameter metadata includes direction, position, domain, field type/subtype, byte length, scale, nullability, and declared default source when present. Procedure source is read from `RDB$PROCEDURE_SOURCE` as text; external procedures may have no source text.
- The order of top-level object categories can be changed from the Organize menu. Only the selected category moves; its child objects remain attached. Category order is persisted in the local application settings file.
- The application theme can be cycled between system default, light, and dark. The selected theme is persisted in the same settings file.
- Initial refresh creates the category nodes and fetches the first 200 objects for every category immediately after connecting (or when manually refreshing). Each catalog page fetch runs on a worker thread; only the resulting page is applied to the UI thread in small chunks that yield to the UI message loop. The status area reports category-by-category progress and warns that large catalogs may take some time, while the window remains responsive. Further pages are fetched automatically as the user scrolls to the end of the currently loaded objects. Pages append to the existing observable child collection, preserving the category node and its current selection.
- Selecting a table or view opens a dedicated editor tab with a field grid, a read-only data browser, constraint details, and (for tables) a generated `CREATE TABLE` draft. Data is fetched in server-side pages of 200 rows; the user can navigate until the end of the table, with no overall row-count ceiling. When a primary key exists, forward/backward pages use lexicographic keyset seeks over its columns, avoiding increasingly expensive deep offsets; tables without a primary key fall back to offset paging and have no guaranteed row order. The draft includes common scalar types, character sets, primary/unique keys, and foreign keys with referential actions where metadata is available. It is explicitly a draft, not a lossless export: domain identity/definitions, defaults, computed expressions, CHECK constraints, collations, triggers, and other dependencies still require separate reconstruction.
- The top-level application actions are organized in File, Connection, Query, and View menus. Query history can be docked on the right or moved into a tab beside the SQL editor.
- The metadata pane has a row splitter so its height can be adjusted independently of the connection list.

## Session and execution behavior

- Metadata is read through the already-open session for the selected profile.
- The metadata call is serialized with connect/disconnect/dispose operations by the session manager.
- Catalog commands and reader iteration use asynchronous provider APIs and accept a cancellation token. Desktop metadata page retrieval is additionally dispatched to a worker thread because provider internals may still perform synchronous work; UI collection updates remain on the UI thread and yield between groups of 25 objects.
- The desktop initially refreshes metadata after a successful connection and provides a manual Refresh action.
- Switching selected profiles clears the displayed list to avoid presenting one database's metadata under another profile.

## Known limitations

- Table columns, table constraints, standalone index segments, stored-procedure parameters/source, and trigger source are loaded on demand. CHECK expressions and NOT NULL constraint source are not yet displayed. Domain validation/default expressions are loaded from the catalog and formatted for display.
- Firebird 3+ packaged procedures are not yet disambiguated by package name in the display model. When duplicate procedure names are detected, parameter/source children are deliberately withheld rather than attached to the wrong procedure.
- Functions, sequences/generators, exceptions, roles, character sets, collations, packages, and privileges are not yet listed.
- Procedure, trigger, domain default/validation, and generated table DDL source displays use the desktop SQL formatter. It formats whitespace, indentation, statement boundaries, and recognized keywords while preserving quoted literals and comments. This is presentation formatting, not a schema rewrite or a lossless SQL normalization.
- No real-server integration fixtures have yet verified the queries on Firebird 2.5, 3, 4, and 5.
- The metadata tree is an expandable inventory, not a metadata editor. Categories expand to objects; tables expand to columns and constraints; key constraints expand to their key columns; indexes expand to ordered segments; procedures expand to parameters and source. Table inspection is provided in a separate tab.
- Metadata category pages are bounded in memory; scrolling automatically fetches the next 200 objects as the end of the loaded list enters view, so the complete category can be browsed without manual pagination controls. Table/view details are loaded on selection rather than during refresh.


## Lazy category loading

Connecting or manually refreshing fetches the first page (up to 200 objects) for every category. Progress is shown while each category is loaded, with an explicit notice that large catalogs may take some time. As the final currently loaded object approaches the visible scroll area, the next 200 rows are fetched and appended to the existing category collection; the category selection is not replaced or closed. No Load next/previous controls are shown. Page size bounds each UI materialization, not the number of discoverable objects. Selecting a table/view loads its columns and constraints, plus view source for views; selecting an index, procedure, or trigger loads its segments, parameters/source, or source respectively.
