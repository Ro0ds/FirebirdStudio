# Firebird compatibility strategy

## Targets

Firebird 2.5, 3.0, 4.0, and 5.0 are product compatibility targets. This does not mean every feature already supports every version.

## Rules

1. Detect and record the actual server version at connection time.
2. Keep version-specific metadata SQL behind Infrastructure adapters.
3. Do not infer server version from client library version.
4. Maintain integration test environments for each supported server major/minor target.
5. Mark a feature unsupported when its required server capability is absent; never silently run a different destructive operation.
6. Test character sets, dialect, identifier quoting, transaction behavior, blob handling, generators/sequences, and monitoring permissions.

## Compatibility matrix

The 0.2.0 candidate was smoke-tested on Windows x64 using Firebird 5.0.4 embedded and a disposable database. This establishes a baseline for the listed scenarios. [0.2.0 validation record](../releasing/0.2.0.md)

| Feature | FB 2.5 | FB 3 | FB 4 | FB 5 |
|---|---|---|---|---|
| Connect/disconnect | Pending | Pending | Pending | Embedded smoke passed. TCP/IP integration pending. |
| Metadata explorer | Pending | Pending | Pending | Embedded paging, search, and table, view, and trigger DDL smoke passed. TCP/IP integration pending. |
| SQL execution | Pending | Pending | Pending | Embedded typed parameters, results, and queued cancellation smoke passed. TCP/IP integration and cancellation of an executing statement pending. |
| Backup/restore | Not implemented | Not implemented | Not implemented | Not implemented |
| Monitoring | Not implemented | Not implemented | Not implemented | Not implemented |

Mark a feature supported for a version and connection mode after repeatable automated integration tests pass. Start with Firebird 5 over TCP/IP using a disposable database, then run the same core checks on Firebird 4, 3, and 2.5.
