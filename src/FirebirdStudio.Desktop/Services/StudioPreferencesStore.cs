using System.Text.Json;

namespace FirebirdStudio.Desktop.Services;

internal sealed record StudioPreferences(
    string Theme,
    IReadOnlyList<string> MetadataCategoryOrder)
{
    public static StudioPreferences Default { get; } = new("Light", Array.Empty<string>());
}

internal sealed class StudioPreferencesStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly string _filePath;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public StudioPreferencesStore(string? filePath = null)
    {
        if (!string.IsNullOrWhiteSpace(filePath))
        {
            _filePath = Path.GetFullPath(filePath);
            return;
        }

        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (string.IsNullOrWhiteSpace(appData))
            appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(appData))
            throw new InvalidOperationException("The operating system did not provide an application data directory.");

        _filePath = Path.Combine(appData, "FirebirdStudio", "settings.json");
    }

    public async Task<StudioPreferences> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!File.Exists(_filePath))
                return StudioPreferences.Default;

            await using var stream = new FileStream(
                _filePath, FileMode.Open, FileAccess.Read, FileShare.Read,
                16 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            var preferences = await JsonSerializer.DeserializeAsync<StudioPreferences>(
                stream, SerializerOptions, cancellationToken).ConfigureAwait(false);
            if (preferences is null)
                throw new InvalidDataException("The saved application settings file is empty or invalid.");

            var theme = preferences.Theme is "Light" or "Dark" ? preferences.Theme : "Light";
            var order = preferences.MetadataCategoryOrder?
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray() ?? Array.Empty<string>();
            return new StudioPreferences(theme, order);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(StudioPreferences preferences, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        if (preferences.Theme is not ("Light" or "Dark"))
            throw new ArgumentException("Theme must be Light or Dark.", nameof(preferences));

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var directory = Path.GetDirectoryName(_filePath)
                ?? throw new InvalidOperationException("The settings path has no parent directory.");
            Directory.CreateDirectory(directory);
            RestrictUnixPermissions(directory, isDirectory: true);

            var temporaryPath = Path.Combine(directory, $".settings.{Guid.NewGuid():N}.tmp");
            try
            {
                await using (var stream = new FileStream(
                    temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                    16 * 1024, FileOptions.Asynchronous | FileOptions.WriteThrough))
                {
                    await JsonSerializer.SerializeAsync(stream, preferences, SerializerOptions, cancellationToken)
                        .ConfigureAwait(false);
                    await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                    stream.Flush(flushToDisk: true);
                }

                RestrictUnixPermissions(temporaryPath, isDirectory: false);
                File.Move(temporaryPath, _filePath, overwrite: true);
                RestrictUnixPermissions(_filePath, isDirectory: false);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                    File.Delete(temporaryPath);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private static void RestrictUnixPermissions(string path, bool isDirectory)
    {
        if (OperatingSystem.IsWindows())
            return;

        File.SetUnixFileMode(path, isDirectory
            ? UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
            : UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }
}
