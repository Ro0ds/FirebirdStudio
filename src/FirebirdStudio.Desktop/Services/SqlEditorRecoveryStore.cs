using System.Text.Json;

namespace FirebirdStudio.Desktop.Services;

internal sealed record SqlEditorRecoverySnapshot(
    int Version,
    DateTimeOffset SavedAt,
    string[] Titles,
    string[] Queries,
    int ActiveEditorIndex);

internal sealed class SqlEditorRecoveryStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly string _filePath;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public SqlEditorRecoveryStore(string? filePath = null)
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

        _filePath = Path.Combine(appData, "FirebirdStudio", "sql-editor-recovery.json");
    }

    public async Task<SqlEditorRecoverySnapshot?> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!File.Exists(_filePath))
                return null;

            await using var stream = new FileStream(
                _filePath, FileMode.Open, FileAccess.Read, FileShare.Read,
                32 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            return await JsonSerializer.DeserializeAsync<SqlEditorRecoverySnapshot>(
                stream, SerializerOptions, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(
        SqlEditorRecoverySnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var directory = Path.GetDirectoryName(_filePath)
                ?? throw new InvalidOperationException("The SQL recovery path has no parent directory.");
            Directory.CreateDirectory(directory);
            RestrictUnixPermissions(directory, isDirectory: true);

            var temporaryPath = Path.Combine(directory, $".sql-editor-recovery.{Guid.NewGuid():N}.tmp");
            try
            {
                await using (var stream = new FileStream(
                    temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                    32 * 1024, FileOptions.Asynchronous | FileOptions.WriteThrough))
                {
                    await JsonSerializer.SerializeAsync(stream, snapshot, SerializerOptions, cancellationToken)
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

    public async Task DeleteAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (File.Exists(_filePath))
                File.Delete(_filePath);
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
