using FirebirdStudio.Core.Connections;

namespace FirebirdStudio.Tests;

internal static class TestProfile
{
    public static ConnectionProfile Create(
        string name = "Test connection",
        Guid? id = null,
        bool pooling = true) => new()
    {
        Id = id ?? Guid.NewGuid(),
        Name = name,
        ServerType = ConnectionServerType.Remote,
        Host = "127.0.0.1",
        Port = 3050,
        Database = "/var/lib/firebird/data/test.fdb",
        UserName = "SYSDBA",
        CharacterSet = "UTF8",
        SqlDialect = 3,
        ConnectionTimeoutSeconds = 5,
        Pooling = pooling
    };
}
