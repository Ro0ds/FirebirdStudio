using System.Text.Json;
using System.Text.Json.Serialization;

namespace FirebirdStudio.Desktop.Services;

internal sealed record QueryHistoryEntry(
    DateTimeOffset ExecutedAt,
    Guid ProfileId,
    string ProfileName,
    string Sql,
    bool Succeeded,
    string Outcome)
{
    [JsonIgnore]
    public string Status => Outcome == "Execution cancelled." ? "Cancelled" : Succeeded ? "Success" : "Failed";

    [JsonIgnore]
    public string Summary =>
        $"{ExecutedAt.ToLocalTime():yyyy-MM-dd HH:mm:ss}  ·  {ProfileName}  ·  {Status}";

    [JsonIgnore]
    public string Preview
    {
        get
        {
            var preview = Sql.Replace('\r', ' ').Replace('\n', ' ').Trim();
            return preview.Length > 240 ? preview[..240] + "…" : preview;
        }
    }
}

internal sealed class QueryHistoryStore
{
    private const int MaximumEntries = 250;
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly string _filePath;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public QueryHistoryStore(string? filePath = null)
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

        _filePath = Path.Combine(appData, "FirebirdStudio", "query-history.json");
    }

    public async Task<IReadOnlyList<QueryHistoryEntry>> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!File.Exists(_filePath))
                return Array.Empty<QueryHistoryEntry>();

            await using var stream = new FileStream(
                _filePath, FileMode.Open, FileAccess.Read, FileShare.Read,
                32 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            var entries = await JsonSerializer.DeserializeAsync<List<QueryHistoryEntry>>(
                stream, SerializerOptions, cancellationToken).ConfigureAwait(false);
            return entries?.Take(MaximumEntries).ToArray() ?? Array.Empty<QueryHistoryEntry>();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(
        IReadOnlyList<QueryHistoryEntry> entries,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entries);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var directory = Path.GetDirectoryName(_filePath)
                ?? throw new InvalidOperationException("The query history path has no parent directory.");
            Directory.CreateDirectory(directory);
            RestrictUnixPermissions(directory, isDirectory: true);

            var temporaryPath = Path.Combine(directory, $".query-history.{Guid.NewGuid():N}.tmp");
            try
            {
                await using (var stream = new FileStream(
                    temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                    32 * 1024, FileOptions.Asynchronous | FileOptions.WriteThrough))
                {
                    var retained = entries.Take(MaximumEntries).ToArray();
                    await JsonSerializer.SerializeAsync(stream, retained, SerializerOptions, cancellationToken)
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
