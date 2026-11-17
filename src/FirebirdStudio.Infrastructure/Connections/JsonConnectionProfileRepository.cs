using System.Text.Json;
using FirebirdStudio.Application.Connections;
using FirebirdStudio.Core.Connections;

namespace FirebirdStudio.Infrastructure.Connections;

/// <summary>
/// Persists non-secret connection metadata in the current user's application data
/// directory. Passwords are intentionally not part of the serialized profile.
/// </summary>
public sealed class JsonConnectionProfileRepository : IConnectionProfileRepository
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly string _filePath;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public JsonConnectionProfileRepository(string? filePath = null)
    {
        if (!string.IsNullOrWhiteSpace(filePath))
        {
            _filePath = Path.GetFullPath(filePath);
            return;
        }

        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (string.IsNullOrWhiteSpace(appData))
            throw new InvalidOperationException("The operating system did not provide an application data directory.");

        _filePath = Path.Combine(appData, "FirebirdStudio", "connections.json");
    }

    public async Task<IReadOnlyList<ConnectionProfile>> GetAllAsync(
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await ReadProfilesAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(
        ConnectionProfile profile,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var validationErrors = ConnectionProfileValidator.Validate(profile);
        if (validationErrors.Count > 0)
            throw new ArgumentException(string.Join(Environment.NewLine, validationErrors), nameof(profile));

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var profiles = (await ReadProfilesAsync(cancellationToken).ConfigureAwait(false)).ToList();
            var existingIndex = profiles.FindIndex(item => item.Id == profile.Id);

            if (existingIndex >= 0)
                profiles[existingIndex] = profile;
            else
                profiles.Add(profile);

            await WriteProfilesAtomicallyAsync(profiles, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task DeleteAsync(Guid profileId, CancellationToken cancellationToken = default)
    {
        if (profileId == Guid.Empty)
            throw new ArgumentException("A valid profile identifier is required.", nameof(profileId));

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var profiles = (await ReadProfilesAsync(cancellationToken).ConfigureAwait(false)).ToList();
            if (profiles.RemoveAll(item => item.Id == profileId) > 0)
                await WriteProfilesAtomicallyAsync(profiles, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<IReadOnlyList<ConnectionProfile>> ReadProfilesAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_filePath))
            return Array.Empty<ConnectionProfile>();

        await using var stream = new FileStream(
            _filePath, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 16 * 1024, options: FileOptions.Asynchronous | FileOptions.SequentialScan);

        var profiles = await JsonSerializer.DeserializeAsync<List<ConnectionProfile>>(
            stream, SerializerOptions, cancellationToken).ConfigureAwait(false);

        if (profiles is null)
            throw new InvalidDataException("The saved connection profile file is empty or invalid.");

        if (profiles.Any(profile => profile is null || ConnectionProfileValidator.Validate(profile).Count > 0))
            throw new InvalidDataException("The saved connection profile file contains an invalid profile.");

        if (profiles.Select(profile => profile.Id).Distinct().Count() != profiles.Count)
            throw new InvalidDataException("The saved connection profile file contains duplicate identifiers.");

        return profiles;
    }

    private async Task WriteProfilesAtomicallyAsync(
        IReadOnlyCollection<ConnectionProfile> profiles,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_filePath)
            ?? throw new InvalidOperationException("The connection profile path has no parent directory.");

        Directory.CreateDirectory(directory);
        RestrictUnixDirectoryPermissions(directory);

        var temporaryPath = Path.Combine(directory, $".connections.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(
                temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                bufferSize: 16 * 1024, options: FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, profiles, SerializerOptions, cancellationToken)
                    .ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }

            RestrictUnixFilePermissions(temporaryPath);
            File.Move(temporaryPath, _filePath, overwrite: true);
            RestrictUnixFilePermissions(_filePath);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    private static void RestrictUnixDirectoryPermissions(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    private static void RestrictUnixFilePermissions(string path)
    {
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }
}
