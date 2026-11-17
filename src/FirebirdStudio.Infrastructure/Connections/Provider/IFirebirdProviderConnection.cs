using FirebirdStudio.Core.Database;
using FirebirdStudio.Application.Connections;

namespace FirebirdStudio.Infrastructure.Connections.Provider;

internal interface IFirebirdProviderConnection : IAsyncDisposable
{
    Task OpenAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<DatabaseMetadataObject>> ReadMetadataAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<DatabaseMetadataObject>> ReadMetadataPageAsync(
        string category, int offset, int pageSize, CancellationToken cancellationToken);
    Task<IReadOnlyList<DatabaseMetadataObject>> SearchMetadataAsync(
        string searchTerm, string? kindFilter, CancellationToken cancellationToken);
    Task<IReadOnlyList<DatabaseMetadataObject>> ReadMetadataObjectDetailsAsync(
        string kind, string name, CancellationToken cancellationToken);
    Task<IReadOnlyList<DatabaseMetadataObject>> ReadSqlCompletionMetadataAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<DatabaseMetadataObject>> ReadSqlCompletionDomainsAsync(CancellationToken cancellationToken);
    Task<DatabaseTableDataPage> ReadTableDataPageAsync(
        string tableName, int offset, int pageSize, IReadOnlyList<object?>? cursorValues,
        bool previous, CancellationToken cancellationToken);
    Task<SqlExecutionResult> ExecuteSqlAsync(string sql, IReadOnlyDictionary<string, object?>? parameters, CancellationToken cancellationToken);
    string ServerVersion { get; }
}

internal interface IFirebirdProviderConnectionFactory
{
    IFirebirdProviderConnection Create(string connectionString);
}
