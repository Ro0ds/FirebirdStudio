using System.Collections.Concurrent;
using FirebirdStudio.Application.Connections;
using FirebirdStudio.Core.Connections;
using FirebirdStudio.Core.Database;
using FirebirdStudio.Infrastructure.Connections.Provider;

namespace FirebirdStudio.Infrastructure.Connections;

/// <summary>
/// Owns one open provider connection per saved profile. Lifecycle operations are serialized
/// to prevent duplicate opens and races with disconnect/disposal.
/// </summary>
public sealed class FirebirdSessionManager : IFirebirdSessionManager, IAsyncDisposable
{
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private readonly ConcurrentDictionary<Guid, ActiveSession> _sessions = new();
    private readonly IFirebirdProviderConnectionFactory _connectionFactory;
    private bool _disposed;

    public FirebirdSessionManager()
        : this(new FirebirdProviderConnectionFactory())
    {
    }

    internal FirebirdSessionManager(IFirebirdProviderConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
    }

    public async Task<ConnectionSessionResult> ConnectAsync(
        ConnectionProfile profile,
        string password,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(password);

        var validationErrors = ConnectionProfileValidator.Validate(profile);
        if (validationErrors.Count > 0)
        {
            var validationMessage = string.Join(Environment.NewLine, validationErrors);
            return new ConnectionSessionResult(false, null, validationMessage,
                $"[Profile validation] Failed{Environment.NewLine}{validationMessage}");
        }

        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (_sessions.ContainsKey(profile.Id))
            {
                var message = $"A session for '{profile.Name}' is already active. Disconnect it before reconnecting.";
                return new ConnectionSessionResult(false, null, message, message);
            }

            IFirebirdProviderConnection? connection = null;
            try
            {
                var connectionString = FirebirdConnectionStringFactory.Build(profile, password);
                connection = _connectionFactory.Create(connectionString);
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

                var info = new ConnectionSessionInfo(
                    profile.Id,
                    profile.Name,
                    connection.ServerVersion,
                    DateTimeOffset.UtcNow);

                if (!_sessions.TryAdd(profile.Id, new ActiveSession(connection, info)))
                    throw new InvalidOperationException("A session for this connection profile was opened concurrently.");

                // Ownership transfers to the active-session dictionary after a successful add.
                connection = null;
                return new ConnectionSessionResult(true, info, "Connection established.");
            }
            catch (OperationCanceledException cancellationException)
            {
                if (connection is not null)
                {
                    try
                    {
                        await connection.DisposeAsync().ConfigureAwait(false);
                    }
                    catch (Exception disposalException)
                    {
                        throw new AggregateException(
                            "The connection attempt was cancelled, but the partially opened connection could not be disposed.",
                            cancellationException,
                            disposalException);
                    }
                }

                throw;
            }
            catch (Exception exception)
            {
                var message = FirebirdConnectionTester.SanitizeMessage(exception.Message, password);
                var diagnostics = FirebirdConnectionTester.SanitizeMessage(exception.ToString(), password);
                if (connection is not null)
                {
                    try
                    {
                        await connection.DisposeAsync().ConfigureAwait(false);
                    }
                    catch (Exception disposalException)
                    {
                        var cleanupMessage = FirebirdConnectionTester.SanitizeMessage(disposalException.ToString(), password);
                        diagnostics += $"{Environment.NewLine}{Environment.NewLine}[Connection cleanup failure]{Environment.NewLine}{cleanupMessage}";
                        var cleanupSummary = FirebirdConnectionTester.SanitizeMessage(disposalException.Message, password);
                        message += $"{Environment.NewLine}Connection cleanup also failed: {cleanupSummary}";
                    }
                }

                return new ConnectionSessionResult(false, null, message, diagnostics);
            }
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task DisconnectAsync(Guid profileId, CancellationToken cancellationToken = default)
    {
        if (profileId == Guid.Empty)
            throw new ArgumentException("A valid connection profile identifier is required.", nameof(profileId));

        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_sessions.TryGetValue(profileId, out var session))
            {
                // Keep the session visible if disposal fails so callers are not told it is closed
                // while the provider connection may still own native resources.
                await session.Connection.DisposeAsync().ConfigureAwait(false);
                _sessions.TryRemove(profileId, out _);
            }
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public IReadOnlyList<ConnectionSessionInfo> GetActiveSessions() =>
        _sessions.Values
            .Select(session => session.Info)
            .OrderBy(session => session.ProfileName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public async Task<IReadOnlyList<DatabaseMetadataObject>> GetMetadataAsync(
        Guid profileId,
        CancellationToken cancellationToken = default)
    {
        if (profileId == Guid.Empty)
            throw new ArgumentException("A valid connection profile identifier is required.", nameof(profileId));

        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!_sessions.TryGetValue(profileId, out var session))
                throw new InvalidOperationException("Connect to this database before loading its metadata.");

            return await session.Connection.ReadMetadataAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task<IReadOnlyList<DatabaseMetadataObject>> GetMetadataPageAsync(
        Guid profileId,
        string category,
        int offset,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        if (profileId == Guid.Empty)
            throw new ArgumentException("A valid connection profile identifier is required.", nameof(profileId));
        if (string.IsNullOrWhiteSpace(category))
            throw new ArgumentException("A metadata category is required.", nameof(category));
        if (offset < 0)
            throw new ArgumentOutOfRangeException(nameof(offset));
        if (pageSize is < 1 or > 500)
            throw new ArgumentOutOfRangeException(nameof(pageSize), "Page size must be between 1 and 500.");

        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!_sessions.TryGetValue(profileId, out var session))
                throw new InvalidOperationException("Connect to this database before loading its metadata.");

            return await session.Connection.ReadMetadataPageAsync(
                category, offset, pageSize, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task<IReadOnlyList<DatabaseMetadataObject>> SearchMetadataAsync(
        Guid profileId,
        string searchTerm,
        CancellationToken cancellationToken = default,
        string? kindFilter = null)
    {
        if (profileId == Guid.Empty)
            throw new ArgumentException("A valid connection profile identifier is required.", nameof(profileId));
        if (string.IsNullOrWhiteSpace(searchTerm))
            throw new ArgumentException("A metadata search term is required.", nameof(searchTerm));

        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!_sessions.TryGetValue(profileId, out var session))
                throw new InvalidOperationException("Connect to this database before searching its metadata.");

            return await session.Connection.SearchMetadataAsync(searchTerm.Trim(), kindFilter, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task<IReadOnlyList<DatabaseMetadataObject>> GetMetadataObjectDetailsAsync(
        Guid profileId, string kind, string name, CancellationToken cancellationToken = default)
    {
        if (profileId == Guid.Empty) throw new ArgumentException("A valid connection profile identifier is required.", nameof(profileId));
        if (string.IsNullOrWhiteSpace(kind)) throw new ArgumentException("An object kind is required.", nameof(kind));
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("An object name is required.", nameof(name));
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!_sessions.TryGetValue(profileId, out var session))
                throw new InvalidOperationException("Connect to this database before loading object details.");
            return await session.Connection.ReadMetadataObjectDetailsAsync(kind, name, cancellationToken).ConfigureAwait(false);
        }
        finally { _operationGate.Release(); }
    }

    public async Task<IReadOnlyList<DatabaseMetadataObject>> GetSqlCompletionMetadataAsync(
        Guid profileId, CancellationToken cancellationToken = default)
    {
        if (profileId == Guid.Empty) throw new ArgumentException("A valid connection profile identifier is required.", nameof(profileId));
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!_sessions.TryGetValue(profileId, out var session))
                throw new InvalidOperationException("Connect to this database before loading SQL completion metadata.");
            return await session.Connection.ReadSqlCompletionMetadataAsync(cancellationToken).ConfigureAwait(false);
        }
        finally { _operationGate.Release(); }
    }

    public async Task<IReadOnlyList<DatabaseMetadataObject>> GetSqlCompletionDomainsAsync(
        Guid profileId, CancellationToken cancellationToken = default)
    {
        if (profileId == Guid.Empty) throw new ArgumentException("A valid connection profile identifier is required.", nameof(profileId));
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!_sessions.TryGetValue(profileId, out var session))
                throw new InvalidOperationException("Connect to this database before loading SQL domain suggestions.");
            return await session.Connection.ReadSqlCompletionDomainsAsync(cancellationToken).ConfigureAwait(false);
        }
        finally { _operationGate.Release(); }
    }

    public async Task<DatabaseTableDataPage> GetTableDataPageAsync(
        Guid profileId, string tableName, int offset, int pageSize,
        IReadOnlyList<object?>? cursorValues = null, bool previous = false,
        CancellationToken cancellationToken = default)
    {
        if (profileId == Guid.Empty) throw new ArgumentException("A valid connection profile identifier is required.", nameof(profileId));
        if (string.IsNullOrWhiteSpace(tableName)) throw new ArgumentException("A table name is required.", nameof(tableName));
        if (offset < 0) throw new ArgumentOutOfRangeException(nameof(offset));
        if (pageSize is < 1 or > 500) throw new ArgumentOutOfRangeException(nameof(pageSize));
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!_sessions.TryGetValue(profileId, out var session))
                throw new InvalidOperationException("Connect to this database before reading table data.");
            return await session.Connection.ReadTableDataPageAsync(
                tableName, offset, pageSize, cursorValues, previous, cancellationToken).ConfigureAwait(false);
        }
        finally { _operationGate.Release(); }
    }

    public async Task<SqlExecutionResult> ExecuteSqlAsync(
        Guid profileId,
        string sql,
        CancellationToken cancellationToken = default,
        IReadOnlyDictionary<string, object?>? parameters = null)
    {
        if (profileId == Guid.Empty)
            throw new ArgumentException("A valid connection profile identifier is required.", nameof(profileId));
        if (string.IsNullOrWhiteSpace(sql))
            throw new ArgumentException("SQL text cannot be empty.", nameof(sql));

        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!_sessions.TryGetValue(profileId, out var session))
                throw new InvalidOperationException("Connect to this database before executing SQL.");

            return await session.Connection.ExecuteSqlAsync(sql, parameters, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _operationGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposed)
                return;

            _disposed = true;
            List<Exception>? errors = null;
            foreach (var pair in _sessions.ToArray())
            {
                if (!_sessions.TryRemove(pair.Key, out var session))
                    continue;

                try
                {
                    await session.Connection.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    (errors ??= []).Add(exception);
                }
            }

            if (errors is { Count: > 0 })
                throw new AggregateException("One or more Firebird sessions could not be disposed cleanly.", errors);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    private sealed record ActiveSession(
        IFirebirdProviderConnection Connection,
        ConnectionSessionInfo Info);
}
