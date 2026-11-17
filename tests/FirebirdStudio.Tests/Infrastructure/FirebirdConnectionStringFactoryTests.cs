using FirebirdSql.Data.FirebirdClient;
using FirebirdStudio.Infrastructure.Connections;
using FirebirdStudio.Core.Connections;

namespace FirebirdStudio.Tests.Infrastructure;

public sealed class FirebirdConnectionStringFactoryTests
{
    [Fact]
    public void RemoteProfileMapsConnectionOptions()
    {
        var profile = TestProfile.Create() with
        {
            Role = "REPORTING",
            PacketSize = 4096,
            FetchSize = 500,
            DatabaseCachePages = 1200,
            MinPoolSize = 2,
            MaxPoolSize = 20,
            ConnectionLifetimeSeconds = 90,
            ConnectionTimeoutSeconds = 35,
            SqlDialect = 2,
            CharacterSet = "WIN1252",
            Pooling = false,
            Compression = true,
            DisableDatabaseTriggers = true,
            DisableGarbageCollection = true
        };

        var connectionString = FirebirdConnectionStringFactory.Build(profile, "test-secret");
        var builder = new FbConnectionStringBuilder(connectionString);

        Assert.Equal(profile.Host, Convert.ToString(builder["Data Source"]));
        Assert.Equal(profile.Port, Convert.ToInt32(builder["Port"]));
        Assert.Equal(profile.Database, Convert.ToString(builder["Database"]));
        Assert.Equal(profile.UserName, Convert.ToString(builder["User ID"]));
        Assert.Equal("test-secret", Convert.ToString(builder["Password"]));
        Assert.Equal(profile.Role, Convert.ToString(builder["Role"]));
        Assert.Equal(profile.CharacterSet, Convert.ToString(builder["Character Set"]));
        Assert.Equal(profile.SqlDialect, Convert.ToInt32(builder["Dialect"]));
        Assert.Equal(profile.ConnectionTimeoutSeconds, Convert.ToInt32(builder["Connection Timeout"]));
        Assert.Equal("Default", Convert.ToString(builder["Server Type"]));
        Assert.Equal(profile.PacketSize, Convert.ToInt32(builder["Packet Size"]));
        Assert.Equal(profile.FetchSize, Convert.ToInt32(builder["Fetch Size"]));
        Assert.Equal(profile.DatabaseCachePages, Convert.ToInt32(builder["Cache Pages"]));
        Assert.Equal(profile.MinPoolSize, Convert.ToInt32(builder["Min Pool Size"]));
        Assert.Equal(profile.MaxPoolSize, Convert.ToInt32(builder["Max Pool Size"]));
        Assert.Equal(profile.ConnectionLifetimeSeconds, Convert.ToInt32(builder["Connection Lifetime"]));
        Assert.Equal(profile.Compression, Convert.ToBoolean(builder["Compression"]));
        Assert.Equal(profile.DisableDatabaseTriggers, Convert.ToBoolean(builder["No Database Triggers"]));
        Assert.Equal(profile.DisableGarbageCollection, Convert.ToBoolean(builder["No Garbage Collect"]));
    }

    [Fact]
    public void ConnectionTestCanForcePoolingOffWithoutChangingProfile()
    {
        var profile = TestProfile.Create(pooling: true);

        var connectionString = FirebirdConnectionStringFactory.Build(profile, "secret", poolingOverride: false);
        var builder = new FbConnectionStringBuilder(connectionString);

        Assert.False(Convert.ToBoolean(builder["Pooling"]));
        Assert.True(profile.Pooling);
    }

    [Fact]
    public void EmbeddedProfileMapsClientLibrary()
    {
        var library = OperatingSystem.IsWindows() ? "fbclient.dll" : "libfbclient.so";
        var profile = TestProfile.Create() with
        {
            ServerType = ConnectionServerType.Embedded,
            ClientLibraryPath = library
        };

        var builder = new FbConnectionStringBuilder(FirebirdConnectionStringFactory.Build(profile, "secret"));

        Assert.Equal("Embedded", Convert.ToString(builder["Server Type"]));
        Assert.Equal(library, Convert.ToString(builder["Client Library"]));
    }
}
