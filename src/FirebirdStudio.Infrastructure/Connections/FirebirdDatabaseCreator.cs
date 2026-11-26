using FirebirdSql.Data.FirebirdClient;
using FirebirdStudio.Core.Connections;

namespace FirebirdStudio.Infrastructure.Connections;

public static class FirebirdDatabaseCreator
{
    public static Task CreateAsync(ConnectionProfile target, string password, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (target.ServerType == ConnectionServerType.Embedded && !Path.IsPathFullyQualified(target.Database))
            throw new ArgumentException("An absolute database file path is required in embedded mode.", nameof(target));

        var connectionString = FirebirdConnectionStringFactory.Build(target, password, poolingOverride: false);
        return Task.Run(() => FbConnection.CreateDatabaseAsync(connectionString, overwrite: false,
            cancellationToken: cancellationToken), cancellationToken);
    }
}
