# Architecture

## Goals

- Cross-platform desktop application for Windows, Linux, and macOS.
- Firebird is the first and currently only supported database engine.
- UI must not contain database access logic.
- Core must not depend on Avalonia, Firebird provider APIs, SQLite, or operating-system APIs.
- Long-running database operations must be asynchronous/cancellable where the underlying API permits.
- Database-changing actions must be explicit and auditable in the UI.

## Dependency direction

```text
Desktop ───────► Application ───────► Core
   │                  ▲
   └────► Infrastructure┘
```

Infrastructure implements application abstractions and may reference Core/Application.
Core has no project references.

## Layers

### Core
Pure domain concepts, invariants, value objects, and database-independent rules.

### Application
Use cases, orchestration, and ports/interfaces. No UI or provider-specific types.

### Infrastructure
Firebird ADO.NET integration, local persistence, file system, process integration, and logging.
Provider-specific types must remain inside this layer.

### Desktop
Avalonia views, view models, navigation, dialogs, and user interaction.

## Important design constraints

- Do not introduce a generic repository merely for abstraction's sake.
- Do not expose provider objects to the UI.
- Do not assume all Firebird versions expose identical metadata or support identical SQL syntax.
- Separate read-only comparison from synchronization.
- A synchronization preview must be generated before applying changes.
- Do not silently commit transactions for the user.
- Avoid loading unbounded result sets into memory.
- Preserve SQL text and parameters separately.
- Credentials are stored through the `ISecureCredentialStore` application port; the current Infrastructure adapter uses Latchkey native OS stores provisionally and must pass platform integration tests before release.
