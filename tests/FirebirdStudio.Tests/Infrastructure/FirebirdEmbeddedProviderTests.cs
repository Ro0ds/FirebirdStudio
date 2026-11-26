using System.Runtime.Loader;
using FirebirdSql.Data.FirebirdClient;
using FirebirdStudio.Infrastructure.Connections;

namespace FirebirdStudio.Tests.Infrastructure;

public sealed class FirebirdEmbeddedProviderTests
{
    [Fact]
    public void EmbeddedConnectionsKeepNativeCapabilityCachesSeparate()
    {
        static string ConnectionString(string library) => new FbConnectionStringBuilder
        {
            ServerType = FbServerType.Embedded,
            ClientLibrary = library,
            Database = "test.fdb",
            UserID = "SYSDBA"
        }.ConnectionString;

        using var older = FirebirdDbConnectionFactory.Create(ConnectionString("older-firebird.dll"));
        using var newer = FirebirdDbConnectionFactory.Create(ConnectionString("newer-firebird.dll"));
        using var anotherOlder = FirebirdDbConnectionFactory.Create(ConnectionString("older-firebird.dll"));

        var olderContext = AssemblyLoadContext.GetLoadContext(older.GetType().Assembly);
        var newerContext = AssemblyLoadContext.GetLoadContext(newer.GetType().Assembly);
        Assert.NotSame(AssemblyLoadContext.Default, olderContext);
        Assert.NotSame(olderContext, newerContext);
        Assert.Same(older.GetType(), anotherOlder.GetType());
    }
}
