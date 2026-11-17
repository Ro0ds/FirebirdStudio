using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace FirebirdStudio.Desktop.Services;

internal sealed record ReleaseAsset(string Name, long SizeBytes, Uri DownloadUrl);

internal sealed record AvailableRelease(
    string TagName,
    Version Version,
    Uri HtmlUrl,
    string? ReleaseNotes,
    IReadOnlyList<ReleaseAsset> Assets);

internal sealed class GitHubReleaseChecker
{
    private static readonly HttpClient HttpClient = CreateHttpClient();
    internal const string LatestReleaseEndpoint =
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
            !string.Equals(htmlUrl.Host, "github.com", StringComparison.OrdinalIgnoreCase) ||
            !htmlUrl.AbsolutePath.StartsWith(
                "/Ro0ds/FirebirdStudio/releases/tag/", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The latest GitHub release returned an unexpected URL.");

        var assets = new List<ReleaseAsset>();
        if (root.TryGetProperty("assets", out var assetsElement) &&
            assetsElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var asset in assetsElement.EnumerateArray())
            {
                var name = asset.TryGetProperty("name", out var nameElement)
                    ? nameElement.GetString()
                    : null;
                var size = asset.TryGetProperty("size", out var sizeElement)
                    ? sizeElement.GetInt64()
                    : 0;
                var downloadUrl = asset.TryGetProperty("browser_download_url", out var urlElement)
                    ? urlElement.GetString()
                    : null;

                if (string.IsNullOrWhiteSpace(name) || size < 0 ||
                    !Uri.TryCreate(downloadUrl, UriKind.Absolute, out var assetUri) ||
                    !IsExpectedReleaseAssetUrl(assetUri, tagName, name))
                    continue;

                assets.Add(new ReleaseAsset(name, size, assetUri));
            }
        }

        var releaseNotes = root.TryGetProperty("body", out var body)
            ? body.GetString()
            : null;

        return new AvailableRelease(tagName, NormalizeVersion(version), htmlUrl, releaseNotes, assets);
    }

    internal static bool IsExpectedReleaseAssetUrl(Uri uri, string tagName, string fileName)
    {
        if (uri.Scheme != Uri.UriSchemeHttps ||
            !string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(fileName, Path.GetFileName(fileName), StringComparison.Ordinal))
            return false;

        var expectedPrefix = $"/Ro0ds/FirebirdStudio/releases/download/{Uri.EscapeDataString(tagName)}/";
        return uri.AbsolutePath.StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(
                   Uri.UnescapeDataString(uri.AbsolutePath[(expectedPrefix.Length)..]),
                   fileName,
                   StringComparison.Ordinal);
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(15)
        };
        var version = typeof(GitHubReleaseChecker).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
        client.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("FirebirdStudio", version));
        client.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        return client;
    }

    internal static Version NormalizeVersion(Version version) =>
        version.Revision <= 0
            ? new Version(version.Major, version.Minor, Math.Max(0, version.Build))
            : version;
}
