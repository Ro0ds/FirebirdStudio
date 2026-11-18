using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

namespace FirebirdStudio.Desktop.Services;

internal sealed record UpdateDownloadProgress(long BytesReceived, long TotalBytes)
{
    public double Percentage => TotalBytes <= 0
        ? 0
        : Math.Clamp(BytesReceived * 100d / TotalBytes, 0, 100);
}

internal sealed record DownloadedUpdate(string FilePath, string DirectoryPath, string FileName, long SizeBytes, string Sha256);

internal sealed class GitHubUpdateDownloader
{
    private const string ManifestFileName = "release-manifest.json";
    private const long MaximumManifestSize = 1_048_576;
    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromMinutes(10)
    };

    public async Task<DownloadedUpdate> DownloadAsync(
        AvailableRelease release,
        IProgress<UpdateDownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        var manifestAsset = release.Assets.SingleOrDefault(asset =>
            string.Equals(asset.Name, ManifestFileName, StringComparison.Ordinal));
        if (manifestAsset is null)
            throw new InvalidDataException(
                $"The release does not contain the required '{ManifestFileName}' asset.");

        if (manifestAsset.SizeBytes <= 0 || manifestAsset.SizeBytes > MaximumManifestSize ||
            !GitHubReleaseChecker.IsExpectedReleaseAssetUrl(
                manifestAsset.DownloadUrl, release.TagName, ManifestFileName))
            throw new InvalidDataException("The release manifest asset has invalid metadata.");

        var manifestBytes = await DownloadManifestAsync(manifestAsset, cancellationToken);
        if (manifestBytes.LongLength != manifestAsset.SizeBytes)
            throw new InvalidDataException("The downloaded release manifest size does not match GitHub asset metadata.");
        var artifact = SelectArtifact(manifestBytes, release);
        var packageAsset = release.Assets.SingleOrDefault(asset =>
            string.Equals(asset.Name, artifact.FileName, StringComparison.Ordinal));

        if (packageAsset is null ||
            packageAsset.SizeBytes != artifact.SizeBytes ||
            !GitHubReleaseChecker.IsExpectedReleaseAssetUrl(
                packageAsset.DownloadUrl, release.TagName, artifact.FileName))
            throw new InvalidDataException(
                $"The release package '{artifact.FileName}' is missing or its metadata does not match the manifest.");

        var updateDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FirebirdStudio",
            "Updates",
            release.Version.ToString());
        Directory.CreateDirectory(updateDirectory);

        var destinationPath = Path.Combine(updateDirectory, artifact.FileName);
        if (File.Exists(destinationPath))
        {
            var existingFile = new FileInfo(destinationPath);
            if (existingFile.Length == artifact.SizeBytes &&
                await ComputeSha256Async(destinationPath, cancellationToken) == artifact.Sha256)
            {
                progress?.Report(new UpdateDownloadProgress(artifact.SizeBytes, artifact.SizeBytes));
                return new DownloadedUpdate(destinationPath, updateDirectory, artifact.FileName, artifact.SizeBytes, artifact.Sha256);
            }

            File.Delete(destinationPath);
        }

        var partialPath = destinationPath + ".download";
        try
        {
            await DownloadPackageAsync(
                packageAsset.DownloadUrl,
                partialPath,
                artifact,
                progress,
                cancellationToken);

            var downloadedInfo = new FileInfo(partialPath);
            if (downloadedInfo.Length != artifact.SizeBytes)
                throw new InvalidDataException(
                    $"The downloaded package size is incorrect. Expected {artifact.SizeBytes:N0} bytes, received {downloadedInfo.Length:N0} bytes.");

            var actualHash = await ComputeSha256Async(partialPath, cancellationToken);
            if (!string.Equals(actualHash, artifact.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(
                    "The downloaded package failed SHA-256 verification. The file was rejected.");

            File.Move(partialPath, destinationPath, overwrite: true);
            progress?.Report(new UpdateDownloadProgress(artifact.SizeBytes, artifact.SizeBytes));
            return new DownloadedUpdate(destinationPath, updateDirectory, artifact.FileName);
        }
        catch
        {
            if (File.Exists(partialPath))
                File.Delete(partialPath);
            throw;
        }
    }

    private static async Task<byte[]> DownloadManifestAsync(
        ReleaseAsset asset,
        CancellationToken cancellationToken)
    {
        using var response = await HttpClient.GetAsync(
            asset.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        if (response.Content.Headers.ContentLength is > MaximumManifestSize)
            throw new InvalidDataException("The release manifest is unexpectedly large.");

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        while (true)
        {
            var count = await stream.ReadAsync(chunk, cancellationToken);
            if (count == 0)
                break;
            if (buffer.Length + count > MaximumManifestSize)
                throw new InvalidDataException("The release manifest is unexpectedly large.");
            await buffer.WriteAsync(chunk.AsMemory(0, count), cancellationToken);
        }

        return buffer.ToArray();
    }

    private static UpdateArtifact SelectArtifact(byte[] manifestBytes, AvailableRelease release)
    {
        using var document = JsonDocument.Parse(manifestBytes);
        var root = document.RootElement;

        if (root.GetProperty("schemaVersion").GetInt32() != 1 ||
            !string.Equals(root.GetProperty("product").GetString(), "FirebirdStudio", StringComparison.Ordinal) ||
            !string.Equals(root.GetProperty("releaseChannel").GetString(), "stable", StringComparison.Ordinal))
            throw new InvalidDataException("The release manifest has an unsupported schema, product, or release channel.");

        var manifestVersionText = root.GetProperty("version").GetString();
        if (!Version.TryParse(manifestVersionText, out var manifestVersion) ||
            GitHubReleaseChecker.NormalizeVersion(manifestVersion) != release.Version)
            throw new InvalidDataException("The release manifest version does not match the GitHub release tag.");

        var operatingSystem = GetOperatingSystem();
        var architecture = GetArchitecture();
        var publishMode = IsSelfContainedDeployment() ? "self-contained" : "framework-dependent";

        var matchingArtifacts = root.GetProperty("artifacts")
            .EnumerateArray()
            .Where(item =>
                string.Equals(item.GetProperty("operatingSystem").GetString(), operatingSystem, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(item.GetProperty("architecture").GetString(), architecture, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(item.GetProperty("publishMode").GetString(), publishMode, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (matchingArtifacts.Length != 1)
            throw new NotSupportedException(
                $"This installation requires a '{publishMode}' {operatingSystem}/{architecture} package, but the release manifest does not contain exactly one matching package.");

        var selected = matchingArtifacts[0];
        var fileName = selected.GetProperty("fileName").GetString();
        var sizeBytes = selected.GetProperty("sizeBytes").GetInt64();
        var sha256 = selected.GetProperty("sha256").GetString();

        if (string.IsNullOrWhiteSpace(fileName) ||
            !string.Equals(fileName, Path.GetFileName(fileName), StringComparison.Ordinal) ||
            !fileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ||
            sizeBytes <= 0 ||
            string.IsNullOrWhiteSpace(sha256) ||
            sha256.Length != 64 ||
            !sha256.All(Uri.IsHexDigit))
            throw new InvalidDataException("The selected release package has invalid manifest metadata.");

        return new UpdateArtifact(fileName, sizeBytes, sha256.ToLowerInvariant());
    }

    private static string GetOperatingSystem() =>
        OperatingSystem.IsWindows() ? "windows" :
        OperatingSystem.IsLinux() ? "linux" :
        OperatingSystem.IsMacOS() ? "macos" :
        throw new NotSupportedException("Automatic package downloads are not supported on this operating system.");

    private static string GetArchitecture() => RuntimeInformation.ProcessArchitecture switch
    {
        Architecture.X64 => "x64",
        Architecture.Arm64 => "arm64",
        Architecture.X86 => "x86",
        _ => throw new NotSupportedException(
            $"Automatic package downloads are not supported for {RuntimeInformation.ProcessArchitecture}.")
    };

    private static bool IsSelfContainedDeployment()
    {
        var hostFxrNames = OperatingSystem.IsWindows()
            ? new[] { "hostfxr.dll" }
            : OperatingSystem.IsMacOS()
                ? new[] { "libhostfxr.dylib" }
                : new[] { "libhostfxr.so" };
        return hostFxrNames.Any(name => File.Exists(Path.Combine(AppContext.BaseDirectory, name)));
    }

    private static async Task DownloadPackageAsync(
        Uri downloadUrl,
        string destinationPath,
        UpdateArtifact artifact,
        IProgress<UpdateDownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        using var response = await HttpClient.GetAsync(
            downloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        if (response.Content.Headers.ContentLength is long contentLength &&
            contentLength != artifact.SizeBytes)
            throw new InvalidDataException(
                $"GitHub reported an unexpected package size ({contentLength:N0} bytes).");

        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var output = new FileStream(
            destinationPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            128 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        var buffer = new byte[128 * 1024];
        long received = 0;
        while (true)
        {
            var count = await input.ReadAsync(buffer, cancellationToken);
            if (count == 0)
                break;

            received += count;
            if (received > artifact.SizeBytes)
                throw new InvalidDataException("The download exceeded the package size declared in the release manifest.");

            await output.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
            progress?.Report(new UpdateDownloadProgress(received, artifact.SizeBytes));
        }

        await output.FlushAsync(cancellationToken);
    }

    private static async Task<string> ComputeSha256Async(
        string filePath,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            filePath, FileMode.Open, FileAccess.Read, FileShare.Read,
            128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private sealed record UpdateArtifact(string FileName, long SizeBytes, string Sha256);
}
