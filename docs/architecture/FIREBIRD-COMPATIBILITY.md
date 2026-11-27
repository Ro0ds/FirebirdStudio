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

The automated TCP/IP scenario passed using Windows x64 test servers on Firebird 2.5.9, 3.0.14, 4.0.7, and 5.0.4. The scenario creates and drops a disposable database. Its scope is recorded below. The Fedora/Podman run is pending.

| Feature | FB 2.5 | FB 3 | FB 4 | FB 5 |
|---|---|---|---|---|
| Connect/disconnect | TCP/IP passed | TCP/IP passed | TCP/IP passed | TCP/IP and embedded smoke passed |
| Metadata explorer | TCP/IP table listing, user/system table search, columns, and table DDL passed | Same TCP/IP scenario passed | Same TCP/IP scenario passed | Same TCP/IP scenario passed. Embedded smoke also covered paging and view and trigger DDL. |
| SQL execution | TCP/IP typed parameters, commit, rollback, cancellation of an active query, and session reuse passed | Same TCP/IP scenario passed | Same TCP/IP scenario passed | Same TCP/IP scenario passed. Embedded smoke also covered queued cancellation. |
| Backup/restore | Not implemented | Not implemented | Not implemented | Not implemented |
| Monitoring | Not implemented | Not implemented | Not implemented | Not implemented |

Mark a feature supported for a version and connection mode after repeatable automated integration tests pass. The [TCP/IP integration guide](../../tests/README.md#real-firebird-tcpip-integration) explains how to run the same scenario on Firebird 5, 4, 3, and 2.5 using disposable databases.
