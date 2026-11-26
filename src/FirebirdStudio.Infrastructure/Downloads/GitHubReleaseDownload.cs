using System.Security.Authentication;

namespace FirebirdStudio.Infrastructure.Downloads;

public static class GitHubReleaseDownload
{
    public static HttpClientHandler CreateHandler() => new()
    {
        SslProtocols = OperatingSystem.IsWindows() ? SslProtocols.Tls12 : SslProtocols.None
    };
}
