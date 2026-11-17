using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace FirebirdStudio.Desktop.Services;

internal sealed record AvailableRelease(string TagName, Version Version, Uri HtmlUrl, string? ReleaseNotes);

internal sealed class GitHubReleaseChecker
{
    private static readonly HttpClient HttpClient = CreateHttpClient();
    private const string LatestReleaseEndpoint =
        "https://api.github.com/repos/Ro0ds/FirebirdStudio/releases/latest";

    public async Task<AvailableRelease?> GetLatestStableReleaseAsync(CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseEndpoint);
        using var response = await HttpClient.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;

        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = document.RootElement;

        if (root.TryGetProperty("draft", out var draft) && draft.GetBoolean() ||
            root.TryGetProperty("prerelease", out var prerelease) && prerelease.GetBoolean())
            return null;

        var tagName = root.GetProperty("tag_name").GetString();
        var releaseUrl = root.GetProperty("html_url").GetString();
        if (string.IsNullOrWhiteSpace(tagName) ||
            !Version.TryParse(tagName.TrimStart('v', 'V'), out var version))
            throw new InvalidDataException("The latest GitHub release has an invalid semantic version tag.");

        if (!Uri.TryCreate(releaseUrl, UriKind.Absolute, out var htmlUrl) ||
            htmlUrl.Scheme != Uri.UriSchemeHttps ||
            !string.Equals(htmlUrl.Host, "github.com", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The latest GitHub release returned an unexpected URL.");

        var releaseNotes = root.TryGetProperty("body", out var body)
            ? body.GetString()
            : null;

        return new AvailableRelease(tagName, NormalizeVersion(version), htmlUrl, releaseNotes);
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(15)
        };
        client.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("FirebirdStudio", "0.1"));
        client.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }

    internal static Version NormalizeVersion(Version version) =>
        version.Revision <= 0
            ? new Version(version.Major, version.Minor, Math.Max(0, version.Build))
            : version;
}
