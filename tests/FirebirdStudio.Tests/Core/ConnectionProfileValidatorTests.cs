using FirebirdStudio.Core.Connections;

namespace FirebirdStudio.Tests.Core;

public sealed class ConnectionProfileValidatorTests
{
    [Fact]
    public void ValidRemoteProfileHasNoErrors()
    {
        var errors = ConnectionProfileValidator.Validate(TestProfile.Create());

        Assert.Empty(errors);
    }

    [Fact]
    public void ValidEmbeddedProfileRequiresAndAcceptsClientLibrary()
    {
        var profile = TestProfile.Create() with
        {
            ServerType = ConnectionServerType.Embedded,
            ClientLibraryPath = OperatingSystem.IsWindows() ? "fbclient.dll" : "libfbclient.so"
        };

        Assert.Empty(ConnectionProfileValidator.Validate(profile));
    }

    [Fact]
    public void EmbeddedProfileWithoutClientLibraryIsRejected()
    {
        var profile = TestProfile.Create() with { ServerType = ConnectionServerType.Embedded };

        Assert.Contains(ConnectionProfileValidator.Validate(profile),
            error => error.Contains("client library path is required", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(65536)]
    public void PortOutsideValidRangeIsRejected(int port)
    {
        var errors = ConnectionProfileValidator.Validate(TestProfile.Create() with { Port = port });

        Assert.Contains(errors, error => error.Contains("Port must be", StringComparison.Ordinal));
    }

    [Fact]
    public void TriggerSuppressionCannotBeCombinedWithPooling()
    {
        var profile = TestProfile.Create() with { DisableDatabaseTriggers = true, Pooling = true };

        Assert.Contains(ConnectionProfileValidator.Validate(profile),
            error => error.Contains("pooling cannot be enabled", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void MinimumPoolSizeCannotExceedMaximumPoolSize()
    {
        var profile = TestProfile.Create() with { MinPoolSize = 10, MaxPoolSize = 5 };

        Assert.Contains(ConnectionProfileValidator.Validate(profile),
            error => error.Contains("Minimum pool size cannot exceed", StringComparison.Ordinal));
    }

    [Fact]
    public void ControlCharactersAreRejectedInHost()
    {
        var profile = TestProfile.Create() with { Host = "server\nmalicious" };

        Assert.Contains(ConnectionProfileValidator.Validate(profile),
            error => error.Contains("control characters", StringComparison.OrdinalIgnoreCase));
    }
}
