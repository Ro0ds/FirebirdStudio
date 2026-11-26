using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using FirebirdStudio.Core.Connections;

namespace FirebirdStudio.Infrastructure.Connections;

public sealed class EmbeddedFirebirdInstaller
{
    private const string ReleaseBase = "https://github.com/FirebirdSQL/firebird/releases/download/";
    private static readonly HttpClient Client = new();
    private static readonly Package[] Packages =
    [
        new("5.0", "5.0.4", "v5.0.4/Firebird-5.0.4.1812-0-windows-x64.zip", "fbclient.dll", 25206603,
            "01e844fce4d5f53272a76205dbc3a1ba4b782ab8e8eadcd808cdbccd9ce13b72"),
        new("4.0", "4.0.7", "v4.0.7/Firebird-4.0.7.3271-0-x64.zip", "fbclient.dll", 24240778,
            "d1ccbff156750ea712635dc49b3a1405fd1c9f23a952b018c885e7ba6af59d06"),
        new("3.0", "3.0.14", "v3.0.14/Firebird-3.0.14.33856-0-x64.zip", "fbclient.dll", 14422413,
            "a070bc5633f9fb5ad261d87d19276324679e8ec77afb03a2bec056ba7e2d2dad"),
        new("2.5", "2.5.9", "R2_5_9/Firebird-2.5.9.27139-0_x64_embed.zip", "fbembed.dll", 5233329,
            "082f2143ca801441a95e2e88d5a06e7e6083d13ef33fcfa8f216ebfa177c71fd")
    ];

    private readonly HttpClient _client;
    private readonly string _root;
    private readonly SemaphoreSlim _installGate = new(1, 1);

    public EmbeddedFirebirdInstaller() : this(Client, Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FirebirdStudio", "engines"))
    {
    }

    internal EmbeddedFirebirdInstaller(HttpClient client, string root)
    {
        _client = client;
        _root = Path.GetFullPath(root);
    }

    public static bool IsSupported => OperatingSystem.IsWindows() && RuntimeInformation.ProcessArchitecture == Architecture.X64;

    public static IReadOnlyList<string> Versions { get; } = Array.AsReadOnly(Packages.Select(package => package.Series).ToArray());

    public async Task<ConnectionProfile> ResolveAsync(ConnectionProfile profile, IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (profile.ServerType != ConnectionServerType.Embedded || profile.EmbeddedEngineVersion is not { } version)
            return profile;

        var library = await InstallAsync(FindPackage(version), progress, cancellationToken).ConfigureAwait(false);
        return profile with { ClientLibraryPath = library, EmbeddedEngineVersion = null };
    }

    public async Task DownloadAllAsync(IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        foreach (var package in Packages)
            await InstallAsync(package, progress, cancellationToken).ConfigureAwait(false);
    }

    private async Task<string> InstallAsync(Package package, IProgress<string>? progress, CancellationToken cancellationToken)
    {
        if (!IsSupported)
            throw new PlatformNotSupportedException("Managed Firebird engines are available on Windows x64.");

        await _installGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var destination = Path.Combine(_root, package.Release);
            var library = Path.Combine(destination, package.ClientLibrary);
            if (File.Exists(library))
                return library;

            progress?.Report(package.Series);
            var staging = Path.Combine(_root, $".install-{Guid.NewGuid():N}");
            Directory.CreateDirectory(staging);
            try
            {
                var archive = Path.Combine(staging, "engine.zip");
                var extracted = Path.Combine(staging, "engine");
                await DownloadAsync(package, archive, cancellationToken).ConfigureAwait(false);
                await ZipFile.ExtractToDirectoryAsync(archive, extracted, cancellationToken).ConfigureAwait(false);
                if (!File.Exists(Path.Combine(extracted, package.ClientLibrary)))
                    throw new InvalidDataException("The Firebird archive is missing its client library.");

                try
                {
                    Directory.Move(extracted, destination);
                }
                catch (IOException) when (File.Exists(library))
                {
                    // Another app process finished installing this exact package.
                }

                return library;
            }
            finally
            {
                Directory.Delete(staging, recursive: true);
            }
        }
        finally
        {
            _installGate.Release();
        }
    }

    private async Task DownloadAsync(Package package, string archive, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(5));
        var token = timeout.Token;
        using var response = await _client.GetAsync(ReleaseBase + package.Asset, HttpCompletionOption.ResponseHeadersRead, token)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var source = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        await using var output = new FileStream(archive, FileMode.CreateNew, FileAccess.Write, FileShare.None,
            81920, FileOptions.Asynchronous);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[81920];
        long size = 0;
        int length;
        while ((length = await source.ReadAsync(buffer, token).ConfigureAwait(false)) > 0)
        {
            size += length;
            if (size > package.Size)
                throw new InvalidDataException("The Firebird archive exceeds its expected size.");
            await output.WriteAsync(buffer.AsMemory(0, length), token).ConfigureAwait(false);
            hash.AppendData(buffer, 0, length);
        }

        if (size != package.Size || !CryptographicOperations.FixedTimeEquals(hash.GetHashAndReset(), Convert.FromHexString(package.Sha256)))
            throw new InvalidDataException("The Firebird archive did not match its pinned SHA-256 checksum and size.");
    }

    private static Package FindPackage(string version) => Packages.FirstOrDefault(package => package.Series == version)
        ?? throw new ArgumentException("The selected Firebird engine version is unavailable.", nameof(version));

    private sealed record Package(string Series, string Release, string Asset, string ClientLibrary, long Size, string Sha256);
}
