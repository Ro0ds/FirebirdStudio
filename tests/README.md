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

The deterministic session tests inject a fake provider connection. The credential adapter tests inject a fake `ILatchkey`; they do not substitute a plaintext backend into production code.

## Real Firebird TCP/IP integration

On Fedora x86-64 with Bash, Podman, and the .NET 10 SDK, run from the repository root:

```bash
bash scripts/test-firebird-tcp.sh        # 5.0, 4.0, 3.0, then 2.5
bash scripts/test-firebird-tcp.sh 5.0    # one version
```

Set `FIREBIRD_TEST_CONTAINER_RUNTIME=docker` to use Docker. The runner starts one disposable container per version with a temporary password and a dynamically assigned loopback port. It removes each container and its anonymous volumes on exit. The images are the [official Firebird images](https://hub.docker.com/r/firebirdsql/firebird) for 5.0.4, 4.0.7, and 3.0.14 and the [archived Firebird 2.5 image](https://github.com/jacobalberty/firebird-docker) for the legacy scenario.

For an existing dedicated test server, set the following environment variables and run the same suite from Linux or Windows.

| Variable | Value |
|---|---|
| `FIREBIRD_TEST_VERSION` | Expected server series, one of `5.0`, `4.0`, `3.0`, or `2.5` |
| `FIREBIRD_TEST_DIRECTORY` | Absolute path **on the server** to a dedicated test directory where Firebird may create and drop databases |
| `FIREBIRD_TEST_PASSWORD` | Test account password |
| `FIREBIRD_TEST_HOST` / `FIREBIRD_TEST_PORT` | Defaults to `127.0.0.1` / `3050` |
| `FIREBIRD_TEST_USER` | Defaults to `SYSDBA`. The account must be allowed to create databases and use their monitoring tables. |

```bash
dotnet test tests/FirebirdStudio.Tests/FirebirdStudio.Tests.csproj -c Release \
  --filter 'Category=FirebirdIntegration' --logger 'console;verbosity=detailed'
```

The test creates a new `studio-it-<guid>.fdb` without overwriting existing files and drops that database after disconnecting. It checks the server version, session lifecycle, user and system metadata, table DDL, typed parameters, commit and rollback visibility from a second connection, and cancellation of a query confirmed active in `MON$STATEMENTS`. It then verifies that the same session can execute another query. Without `FIREBIRD_TEST_VERSION`, the integration test is skipped in ordinary test and packaging runs.

## Platform integration tests still required

Run credential-store round-trip, overwrite, delete, missing-key, locked/unavailable-store, and persistence tests on real Windows Credential Manager, macOS Keychain, and Linux Secret Service environments. Linux CI must provide a D-Bus session and Secret Service implementation (for example GNOME Keyring). Use unique disposable profile IDs and verify that no credential appears in the JSON profile file.

## Verification status

All 96 tests passed on Windows in Release configuration for v0.1.4. The Windows packaging workflow runs the suite before creating release assets. Run `python scripts/verify-structure.py` for the additional source and project structure checks.
