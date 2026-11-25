# Automated test coverage

Run from the solution root with the .NET 10 SDK:

```powershell
dotnet test
```

The `FirebirdStudio.Tests` project currently contains deterministic unit tests for:

- Remote and embedded profile validation, invalid ports, control characters, pool bounds, and trigger/pooling constraints.
- JSON profile save/load/update/delete, concurrent saves within one repository instance, absence of password fields, malformed JSON, duplicate identifiers, and temporary-file cleanup.
- Firebird provider connection-string mapping for remote and embedded profiles, including the no-pooling override used by connection tests.
- Credential adapter save/get/delete, missing entries, stable profile-scoped keys, invalid profile identifiers, pre-cancelled operations, and zeroing of the retrieved byte buffer.
- Session connect/disconnect, duplicate session rejection, password redaction, cancellation and disposal, pooling configuration, and manager disposal behavior.
- SQL completion, SQL formatting, generated table DDL, and catalog source previews for views, triggers, procedures, and domains. Multiline comparisons normalize line endings to support LF and CRLF checkouts.

The session manager uses an internal provider-connection factory seam. Tests inject a fake provider connection; they do not connect to a Firebird server. The credential adapter tests inject a fake `ILatchkey`; they do not substitute a plaintext backend into production code.

## Platform integration tests still required

Run credential-store round-trip, overwrite, delete, missing-key, locked/unavailable-store, and persistence tests on real Windows Credential Manager, macOS Keychain, and Linux Secret Service environments. Linux CI must provide a D-Bus session and Secret Service implementation (for example GNOME Keyring). Use unique disposable profile IDs and verify that no credential appears in the JSON profile file.

Real Firebird integration tests must use explicitly disposable test databases and must never run destructive operations against user databases. Add version-specific fixtures for Firebird 2.5, 3, 4, and 5 after deterministic unit tests are passing.

## Verification status

All 96 tests passed on Windows in Release configuration for v0.1.4. The Windows packaging workflow runs the suite before creating release assets. Run `python scripts/verify-structure.py` for the additional source and project structure checks.
