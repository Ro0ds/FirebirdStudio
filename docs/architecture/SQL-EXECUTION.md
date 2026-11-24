# SQL execution and query history

## Current behavior

- SQL is executed only on the currently selected, active connection.
- Each execution submits one statement through the Firebird ADO.NET provider. Multi-statement scripts are not supported yet.
- A provider transaction is opened for the statement. The transaction is committed after successful execution; rollback is attempted when execution fails or is cancelled.
- The first result set is displayed in a read-only TableView. At most 500 rows and approximately 2 million characters are materialized; one additional row is read only to determine whether the result was truncated.
- Text values are clipped to 4,000 characters in the preview. BLOB and ARRAY values are represented by placeholders to avoid eagerly materializing large values; other binary values are represented by a maximum 64-byte hexadecimal preview.
- Named parameters written as `:PARAMETER_NAME` are detected outside SQL string literals, quoted identifiers, and comments. Before execution, a dialog asks for each value and its type (Text, Integer, Decimal, Date, Time, Timestamp, Boolean, or NULL). Placeholders are normalized for the Firebird ADO.NET provider and values are bound as parameters; they are not interpolated into SQL text.
- The Cancel action requests cancellation through the provider token. The server/provider may take time to honor cancellation.
- The SQL editor can format the selected SQL text, or the full document when there is no selection, using Query → Format SQL (`Ctrl+Shift+F`). The formatter currently applies token-aware spacing and common DML/DDL clause layout; it is not a complete Firebird parser or lossless schema formatter. Review generated output, especially procedural DDL, before executing it. The table inspector displays its generated DDL directly. View sources use query-formatting rules, trigger and procedure sources use procedural formatting, and domain default and validation expressions retain their catalog text.
- Errors automatically select the Messages page and display a temporary footer notification. The Messages page keeps the latest 500 success, information, and error events in reverse chronological order, each with a timestamp and soft green/yellow/red severity styling. Error entries retain exception types, stack traces, and inner-exception details (with long individual details truncated at 30,000 characters). Successful SQL execution adds a green entry and switches from Messages to Results only when Messages was the selected page.

## Persistent history

- The latest 250 submitted executions are stored in `query-history.json` under the application data directory.
- Both successful and failed/cancelled attempts are retained.
- The exact SQL text is stored so a double-click can restore it without changing whitespace or literals. Bound parameter values are not stored; restored parameterized queries prompt again before execution.
- Entries retain execution time, connection profile ID/name, outcome, and SQL text. Passwords and connection strings are not written by the history store.
- SQL literals may themselves contain sensitive values. The history is not a secret store; do not embed credentials or other secrets in SQL text.
- The file is written through a temporary file and atomically replaced. On Unix-like systems, directory/file permissions are restricted to the current user.

## Not yet supported

Multi-statement scripts, multiple result sets, structured/editable result grids, query plans, and transaction controls (begin/commit/rollback as explicit editor commands) remain future work. Real-server tests are required across Firebird 2.5, 3, 4, and 5 before this phase can be considered complete.


## Result presentation and table inspection

The Results tab shows the execution state and, when available, the row count and elapsed time. It marks the preview as partial when the result exceeds the row or aggregate data limit. The read-only grid uses columns generated from the provider result metadata. Click a column header to sort the loaded rows in ascending order. Click it again for descending order. Use `ORDER BY` in the SQL statement to order the full result on the server. Resize columns to inspect long values. Use **Copy row** in the toolbar or a row's context menu to copy the preview values as tab-separated text. An empty result set still displays its column headers.

Table selection opens a table-inspection tab containing field/domain/type/nullability/position metadata, paged table data, constraint information, and a generated `CREATE TABLE` draft. The DDL is a convenience preview, not a lossless schema export: domain definitions, defaults, computed expressions, triggers, check constraints, collations, and other database objects are not all reconstructed. Review it before use.

F9 executes the selected SQL text by default. With no selection, it executes the editor document when it contains one statement. If multiple statements are present, select one to execute. To choose a different key, open **File → Settings**, select a key under **Execute SQL**, and save. **Restore default** selects F9 again. The selected shortcut appears in **Query → Execute** and in the editor. Query history can be docked to the right or moved into a tab alongside the SQL editor.


## SQL autocomplete and table data browsing

- `Ctrl+Space` opens SQL completion for the active database. The first request loads and caches relation/column names, procedures, triggers, indexes, sequences, exceptions, functions, character sets, collations, and roles. The domain catalog is fetched separately only in a data-type context, and only after the user types a prefix; this avoids opening a completion popup containing thousands of domains when the prefix is empty. Refreshing metadata invalidates that profile's completion cache.
- Completion includes Firebird SQL keywords, common built-in functions and data types. It recognizes basic aliases declared after `FROM`, `JOIN`, `UPDATE`, `INTO`, and `REFERENCES`; typing `alias.` offers the columns of the resolved relation.
- Table/view data browsing fetches 200 rows per request using Firebird `ROWS` pagination. Primary-key columns are used for forward/backward keyset seeks when available, avoiding deep-offset scans. Without a primary key, the browser falls back to offset paging and row order is not guaranteed. This is separate from the SQL editor's query-result preview limits.


## SQL editor workspace and crash recovery

- Open SQL editor tabs, tab titles, query contents, and the active editor index are autosaved 700 ms after typing stops. `Ctrl+S` also writes the recovery snapshot immediately. Successful writes show a temporary centered footer confirmation (`Editor autosaved` or `Editor saved`).
- Recovery snapshots are stored in `sql-editor-recovery.json` under the application-data directory. Writes use a temporary file and atomic replacement; Unix permissions are restricted to the current user.
- When a recovery snapshot is found at startup, all saved editor tabs are restored and the Messages page displays a notification. Restored SQL is never executed automatically.
- Normal shutdown saves the latest workspace and retains it for the next launch. After an unexpected shutdown, the next launch restores the most recent saved snapshot.
- The recovery file contains SQL text and is local application data, not a secret store. Avoid embedding credentials or other secrets in SQL statements.
