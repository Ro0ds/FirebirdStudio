# Security requirements

- Never log passwords, full connection strings, or secret-bearing command lines.
- Connection profile persistence must contain metadata only, never passwords.
- Persistent credentials must use Windows Credential Manager, macOS Keychain Services, or Linux Secret Service (or another reviewed OS-protected facility).
- If the OS credential facility is unavailable, locked, or rejects access, fail closed. Never silently write plaintext credentials.
- Remembered passwords are stored through Latchkey using the native OS credential facility: Windows Credential Manager, macOS Keychain, or Linux Secret Service. The application uses Latchkey's default native backend only; it does not configure its plaintext file backend or any fallback backend. Native calls are moved to a worker thread because an OS credential prompt can block; cancellation cannot interrupt a native call already in progress. If the native store is unavailable, the user can still enter a password for a one-session connection without saving it.
- Avalonia does not provide a dedicated `PasswordBox`; the UI uses a `TextBox` with `PasswordChar` masking and undo disabled. This is visual masking, not secure-memory storage.
- The Firebird ADO.NET provider receives a password-bearing connection string in process memory; do not log or persist it.
- Clearing a password control removes its displayed value but cannot guarantee zeroization of immutable managed strings or copies held by provider internals.
- Suppressing database triggers and disabling garbage collection are advanced opt-in connection settings. Suppressing triggers may bypass database-side business rules; do not enable it for ordinary application work.
- Do not embed default credentials.
- Use least-privilege database accounts for ordinary browsing.
- Make destructive actions visually distinct and require confirmation.
- Use parameterized SQL for values; identifiers require a separate, strict quoting/validation strategy.
- Treat imported SQL, metadata, filenames, and server messages as untrusted data.
- Do not execute SQL automatically when opening a file or connecting.
- Support explicit transaction control.
- Keep backups and exports outside application-managed temporary directories unless the user chooses otherwise.
- Record security-relevant actions without recording secrets.
- Active database sessions are held only in process memory. The session manager owns provider connections and disposes them when disconnected or when the main window closes. A remembered password is retrieved only for its profile ID and may be removed by unchecking the remember option on a successful connection. The Firebird provider still receives a managed password string and a password-bearing connection string in process memory; immutable managed strings and provider-owned copies cannot be reliably zeroized. The adapter zeroes the temporary UTF-8 byte buffers it allocates after native store operations. If a profile is deleted while the OS vault is unavailable, its secret may remain as an orphaned vault entry; the UI reports this condition rather than claiming it was deleted.

## SQL editor recovery and diagnostic messages

SQL editor recovery stores the contents and titles of open editor tabs in the current user's application-data directory so they can be restored after an unexpected shutdown. The snapshot is removed after a normal application exit. As with query history, SQL text may contain sensitive literal values; it must not be treated as a credential store. Connection diagnostic output redacts the exact password supplied to the connection attempt before displaying provider exception details.
