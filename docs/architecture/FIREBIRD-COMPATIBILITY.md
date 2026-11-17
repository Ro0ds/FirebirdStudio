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

| Feature | FB 2.5 | FB 3 | FB 4 | FB 5 |
|---|---|---|---|---|
| Connect/disconnect | Not tested | Not tested | Not tested | Not tested |
| Metadata explorer | Implemented; integration unverified | Implemented; integration unverified | Implemented; integration unverified | Implemented; integration unverified |
| SQL execution | Implemented; integration unverified | Implemented; integration unverified | Implemented; integration unverified | Implemented; integration unverified |
| Backup/restore | Not implemented | Not implemented | Not implemented | Not implemented |
| Monitoring | Not implemented | Not implemented | Not implemented | Not implemented |

Do not change a cell to supported until automated integration evidence exists.
