using FirebirdStudio.Core.Connections;
using FirebirdStudio.Infrastructure.Connections;

namespace FirebirdStudio.Tests.Infrastructure;

public sealed class FirebirdConnectionTesterTests
{
    [Fact]
    public async Task InvalidProfileReturnsValidationDiagnosticsWithoutNetworkAccess()
    {
        var profile = TestProfile.Create() with { Host = string.Empty };

        var result = await new FirebirdConnectionTester().TestAsync(profile, "secret");

        Assert.False(result.Succeeded);
        Assert.Contains("Connection profile validation failed", result.Message, StringComparison.Ordinal);
        Assert.Contains("[Profile validation] Failed", result.Diagnostics!, StringComparison.Ordinal);
        Assert.Contains("Server host is required", result.Diagnostics!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EmbeddedConnectionReportsMissingClientLibrary()
    {
        var profile = TestProfile.Create() with
        {
            ServerType = ConnectionServerType.Embedded,
            ClientLibraryPath = Path.Combine(Path.GetTempPath(), $"missing-fbclient-{Guid.NewGuid():N}.dll")
        };

        var result = await new FirebirdConnectionTester().TestAsync(profile, "secret");

        Assert.False(result.Succeeded);
        Assert.Contains("embedded client library", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("[Embedded client library]", result.Diagnostics!, StringComparison.Ordinal);
    }
}
