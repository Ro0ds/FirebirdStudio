using FirebirdSql.Data.FirebirdClient;
using FirebirdStudio.Core.Connections;

namespace FirebirdStudio.Infrastructure.Connections;

/// <summary>Builds provider connection strings consistently for tests and active sessions.</summary>
internal static class FirebirdConnectionStringFactory
{
    public static string Build(
        ConnectionProfile profile,
        string password,
        bool? poolingOverride = null)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(password);

        var errors = ConnectionProfileValidator.Validate(profile);
        if (errors.Count > 0)
            throw new ArgumentException(string.Join(Environment.NewLine, errors), nameof(profile));
        if (profile.EmbeddedEngineVersion is not null)
            throw new InvalidOperationException("Resolve the managed Firebird engine before building the connection string.");

        var builder = new FbConnectionStringBuilder
        {
            ["Data Source"] = profile.ServerType == ConnectionServerType.Embedded
                ? string.Empty
                : profile.Host,
            ["Port"] = profile.Port,
            ["Database"] = profile.Database,
            ["User ID"] = profile.UserName,
            ["Password"] = password,
            ["Role"] = profile.Role ?? string.Empty,
            ["Character Set"] = profile.CharacterSet,
            ["Dialect"] = profile.SqlDialect,
            ["Server Type"] = profile.ServerType == ConnectionServerType.Embedded ? "Embedded" : "Default",
            ["Packet Size"] = profile.PacketSize,
            ["Fetch Size"] = profile.FetchSize,
            ["Cache Pages"] = profile.DatabaseCachePages,
            ["Pooling"] = poolingOverride ?? profile.Pooling,
            ["Min Pool Size"] = profile.MinPoolSize,
            ["Max Pool Size"] = profile.MaxPoolSize,
            ["Connection Lifetime"] = profile.ConnectionLifetimeSeconds,
            ["Connection Timeout"] = profile.ConnectionTimeoutSeconds,
            ["Compression"] = profile.Compression,
            ["No Database Triggers"] = profile.DisableDatabaseTriggers,
            ["No Garbage Collect"] = profile.DisableGarbageCollection
        };

        if (profile.ServerType == ConnectionServerType.Embedded)
            builder["Client Library"] = profile.ClientLibraryPath!;

        return builder.ConnectionString;
    }
}
