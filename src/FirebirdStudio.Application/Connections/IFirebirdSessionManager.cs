using FirebirdStudio.Core.Connections;
using FirebirdStudio.Core.Database;

namespace FirebirdStudio.Application.Connections;

public sealed record ConnectionSessionInfo(
    Guid ProfileId,
    string ProfileName,
    string ServerVersion,
    DateTimeOffset ConnectedAt);

public sealed record ConnectionSessionResult(
    bool Succeeded,
    ConnectionSessionInfo? Session,
    string Message,
    string? Diagnostics = null);

public sealed record SqlExecutionResult(
    IReadOnlyList<string> Columns,
    IReadOnlyList<IReadOnlyList<string>> Rows,
    int RowsAffected,
    bool HasMoreRows,
    TimeSpan Elapsed)
{
    /// <summary>Comparable values for the materialized cells, with null for SQL NULL or unavailable values.</summary>
    public IReadOnlyList<IReadOnlyList<IComparable?>>? SortValues { get; init; }
}

/// <summary>Owns long-lived Firebird sessions independently from connection testing.</summary>
public interface IFirebirdSessionManager
{
    Task<ConnectionSessionResult> ConnectAsync(
        ConnectionProfile profile,
        string password,
        CancellationToken cancellationToken = default);

    Task DisconnectAsync(Guid profileId, CancellationToken cancellationToken = default);

    IReadOnlyList<ConnectionSessionInfo> GetActiveSessions();

    Task<IReadOnlyList<DatabaseMetadataObject>> GetMetadataAsync(
        Guid profileId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DatabaseMetadataObject>> GetMetadataPageAsync(
        Guid profileId,
        string category,
        int offset,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DatabaseMetadataObject>> SearchMetadataAsync(
        Guid profileId,
        string searchTerm,
        CancellationToken cancellationToken = default,
        string? kindFilter = null);

    Task<IReadOnlyList<DatabaseMetadataObject>> GetMetadataObjectDetailsAsync(
        Guid profileId,
        string kind,
        string name,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DatabaseMetadataObject>> GetSqlCompletionMetadataAsync(
        Guid profileId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DatabaseMetadataObject>> GetSqlCompletionDomainsAsync(
        Guid profileId,
        CancellationToken cancellationToken = default);

    Task<DatabaseTableDataPage> GetTableDataPageAsync(
        Guid profileId,
        string tableName,
        int offset,
        int pageSize,
        IReadOnlyList<object?>? cursorValues = null,
        bool previous = false,
        CancellationToken cancellationToken = default);

    Task<SqlExecutionResult> ExecuteSqlAsync(
        Guid profileId,
        string sql,
        CancellationToken cancellationToken = default,
        IReadOnlyDictionary<string, object?>? parameters = null);
}
