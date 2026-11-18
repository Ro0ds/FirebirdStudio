using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace FirebirdStudio.Updater;

internal sealed class MainWindow : Window
{
    private const int MaxEntries = 20_000;
    private const long MaxEntryBytes = 1_073_741_824;
    private const long MaxExpandedBytes = 2_147_483_648;
    private readonly Dictionary<string, string> _args;
    private readonly TextBlock _status;
    private readonly TextBlock _details;
    private readonly ProgressBar _progress;
    private readonly Button _launchButton;
    private string? _appPath;
    private string? _installDirectory;
    private string? _logPath;

    public MainWindow(string[] args)
    {
        _args = ParseArguments(args);
        Title = "Firebird Studio Updater";
        Width = 500;
        SizeToContent = SizeToContent.Height;
        MinHeight = 220;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        _status = new TextBlock { Text = "Preparing update…", FontSize = 16, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap };
        _details = new TextBlock { Text = "Firebird Studio will close while its files are updated.", TextWrapping = TextWrapping.Wrap };
        _progress = new ProgressBar { Minimum = 0, Maximum = 100, Height = 12 };
        _launchButton = new Button { Content = "Launch Firebird Studio", IsVisible = false };
        _launchButton.Click += (_, _) => TryLaunchApplication();
        Content = new StackPanel
        {
            Margin = new Thickness(22),
            Spacing = 14,
            Children =
            {
                _status, _details, _progress,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Children = { _launchButton }
                }
            }
        };
        Opened += async (_, _) => await RunUpdateAsync();
    }

    private async Task RunUpdateAsync()
    {
        string? staging = null;
        try
        {
            var parentPid = RequiredInt("--parent-pid");
            var archive = Path.GetFullPath(Required("--archive"));
            var expectedSize = RequiredLong("--expected-size");
            var expectedHash = Required("--expected-sha256").ToLowerInvariant();
            _installDirectory = Path.GetFullPath(Required("--install-dir"));
            _appPath = Path.GetFullPath(Required("--app-path"));
            _logPath = Path.GetFullPath(Required("--log-path"));
            if (expectedSize <= 0 || expectedHash.Length != 64 || !expectedHash.All(Uri.IsHexDigit))
                throw new InvalidDataException("The expected package size or SHA-256 value is invalid.");
            if (!Directory.Exists(_installDirectory))
                throw new DirectoryNotFoundException("The application installation directory does not exist.");
            EnsureApplicationInsideInstallDirectory();
            Directory.CreateDirectory(Path.GetDirectoryName(_logPath)!);
            await LogAsync("Updater started.");

            SetStatus("Waiting for Firebird Studio to close…", "No installation files will be modified until the application exits.", 5);
            await WaitForParentExitAsync(parentPid);
            SetStatus("Verifying update package…", "Checking the downloaded ZIP size and SHA-256.", 12);
            await VerifyPackageAsync(archive, expectedSize, expectedHash);
            await LogAsync("Package size and SHA-256 verified.");

            staging = Path.Combine(Path.GetDirectoryName(archive)!, "staging-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(staging);
            SetStatus("Validating and extracting package…", "Checking archive paths and contents before installation.", 20);
            var files = await ExtractSafelyAsync(archive, staging);
            var backup = Path.Combine(Path.GetDirectoryName(archive)!, "backup-" + DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss"));
            Directory.CreateDirectory(backup);
            var applied = new List<AppliedFile>();

            try
            {
                SetStatus("Installing update…", "Existing files are backed up before replacement.", 55);
                await InstallAsync(files, staging, backup, applied);
            }
            catch (Exception installError)
            {
                await LogAsync("Installation failed: " + installError);
                SetStatus("Installation failed. Rolling back…", "Restoring the previous application files.", 80);
                var errors = await RollbackAsync(applied);
                if (errors.Count == 0)
                {
                    await LogAsync("Rollback completed successfully.");
                    SetStatus("Update failed; previous version restored.", "Restarting Firebird Studio with the previous files.", 100);
                    TryLaunchApplication();
                    return;
                }

                foreach (var error in errors)
                    await LogAsync("Rollback error: " + error);
                _status.Text = "Update and rollback failed";
                _details.Text = "Automatic rollback was incomplete. Firebird Studio was not restarted. Keep this backup and updater.log for recovery:" +
                                Environment.NewLine + backup + Environment.NewLine + _logPath +
                                Environment.NewLine + Environment.NewLine + installError.Message;
                return;
            }

            await LogAsync("Installation completed. Backup retained at " + backup);
            SetStatus("Update installed successfully.", "Starting the updated version of Firebird Studio.", 100);
            TryLaunchApplication();
        }
        catch (Exception exception)
        {
            await LogAsync("Updater error: " + exception);
            _status.Text = "The update could not be installed.";
            _details.Text = exception.Message + Environment.NewLine + Environment.NewLine +
                            "The existing application files were not intentionally removed. You can launch Firebird Studio again.";
            _launchButton.IsVisible = true;
        }
        finally
        {
            if (staging is not null)
            {
                try { if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true); }
                catch (Exception exception) { await LogAsync("Could not remove staging directory: " + exception.Message); }
            }
        }
    }

    private async Task WaitForParentExitAsync(int processId)
    {
        try
        {
            using var parent = Process.GetProcessById(processId);
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            try { await parent.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested)
            {
                throw new TimeoutException("Firebird Studio did not close within two minutes. No files were changed.");
            }
        }
        catch (ArgumentException) { }
    }

    private static async Task VerifyPackageAsync(string path, long size, string hash)
    {
        var file = new FileInfo(path);
        if (!file.Exists || file.Length != size)
            throw new InvalidDataException("The downloaded ZIP is missing or its size no longer matches release metadata.");
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var actual = Convert.ToHexString(await SHA256.HashDataAsync(stream)).ToLowerInvariant();
        if (!string.Equals(actual, hash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The downloaded ZIP failed SHA-256 verification. No files were changed.");
    }

    private async Task<List<string>> ExtractSafelyAsync(string archivePath, string staging)
    {
        using var archive = ZipFile.OpenRead(archivePath);
        if (archive.Entries.Count > MaxEntries)
            throw new InvalidDataException($"The update contains too many ZIP entries (maximum {MaxEntries:N0}).");
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var files = new List<string>();
        long expanded = 0;
        foreach (var entry in archive.Entries)
        {
            var relative = ValidateEntry(entry);
            if (relative is null) continue;
            if (!seen.Add(relative))
                throw new InvalidDataException($"The update contains a duplicate path: {relative}");
            if (entry.Length < 0 || entry.Length > MaxEntryBytes)
                throw new InvalidDataException($"The update entry is too large: {relative}");
            expanded = checked(expanded + entry.Length);
            if (expanded > MaxExpandedBytes)
                throw new InvalidDataException("The update expands beyond the 2 GB safety limit.");

            var destination = Path.GetFullPath(Path.Combine(staging, relative));
            EnsurePathInside(staging, destination);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            await using var input = entry.Open();
            await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            var buffer = new byte[128 * 1024];
            long written = 0;
            while (true)
            {
                var count = await input.ReadAsync(buffer);
                if (count == 0) break;
                written += count;
                if (written > entry.Length || written > MaxEntryBytes)
                    throw new InvalidDataException($"Extracted data exceeded the declared size: {relative}");
                await output.WriteAsync(buffer.AsMemory(0, count));
            }
            if (written != entry.Length)
                throw new InvalidDataException($"Extracted size did not match ZIP metadata: {relative}");
            if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
            {
                var unixMode = (UnixFileMode)(((uint)entry.ExternalAttributes >> 16) & 0x1FF);
                if (unixMode != 0)
                    File.SetUnixFileMode(destination, unixMode);
            }
            files.Add(relative);
        }
        if (files.Count == 0) throw new InvalidDataException("The update archive contains no files.");
        return files;
    }

    private static string? ValidateEntry(ZipArchiveEntry entry)
    {
        var raw = entry.FullName.Replace('\\', '/');
        var isDirectory = raw.EndsWith("/", StringComparison.Ordinal);
        var relative = ValidateRelativePath(isDirectory ? raw.TrimEnd('/') : raw);
        var unixMode = ((uint)entry.ExternalAttributes >> 16) & 0xF000;
        var attributes = (FileAttributes)(entry.ExternalAttributes & 0xFFFF);
        if (unixMode == 0xA000 || (attributes & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException($"Symbolic links and reparse points are not allowed: {relative}");
        if (isDirectory) return null;

        string[] forbidden = [".fdb", ".gdb", ".ib", ".sqlite", ".sqlite3", ".db", ".db3", ".backup", ".fbk", ".gbk"];
        if (forbidden.Contains(Path.GetExtension(relative), StringComparer.OrdinalIgnoreCase))
            throw new InvalidDataException($"Database and backup files are not allowed in updates: {relative}");
        return relative;
    }

    private static string ValidateRelativePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.StartsWith("/", StringComparison.Ordinal) ||
            Path.IsPathRooted(path) || path.Contains(':'))
            throw new InvalidDataException($"The update contains an absolute or invalid path: {path}");
        var segments = path.Split('/');
        foreach (var segment in segments)
        {
            if (segment.Length == 0 || segment is "." or ".." ||
                segment.EndsWith(".", StringComparison.Ordinal) || segment.EndsWith(" ", StringComparison.Ordinal))
                throw new InvalidDataException($"The update contains an unsafe path: {path}");
            var device = segment.Split('.')[0];
            if (device.Equals("CON", StringComparison.OrdinalIgnoreCase) ||
                device.Equals("PRN", StringComparison.OrdinalIgnoreCase) ||
                device.Equals("AUX", StringComparison.OrdinalIgnoreCase) ||
                device.Equals("NUL", StringComparison.OrdinalIgnoreCase) ||
                (device.Length == 4 &&
                 (device.StartsWith("COM", StringComparison.OrdinalIgnoreCase) ||
                  device.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)) &&
                 device[3] is >= '1' and <= '9'))
                throw new InvalidDataException($"The update contains a reserved Windows path: {path}");
        }
        return Path.Combine(segments);
    }

    private async Task InstallAsync(IReadOnlyList<string> files, string staging, string backup, List<AppliedFile> applied)
    {
        foreach (var relative in files)
        {
            var source = Path.Combine(staging, relative);
            var destination = Path.GetFullPath(Path.Combine(_installDirectory!, relative));
            EnsurePathInside(_installDirectory!, destination);
            EnsureNoReparsePoints(_installDirectory!, destination);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            var existed = File.Exists(destination);
            var backupPath = Path.Combine(backup, relative);
            if (existed)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(backupPath)!);
                File.Copy(destination, backupPath, overwrite: false);
                if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
                    File.SetUnixFileMode(backupPath, File.GetUnixFileMode(destination));
            }
            applied.Add(new AppliedFile(destination, backupPath, existed));
            var temporary = destination + ".firebirdstudio-update-" + Guid.NewGuid().ToString("N");
            try
            {
                File.Copy(source, temporary, overwrite: false);
                if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
                    File.SetUnixFileMode(temporary, File.GetUnixFileMode(source));
                File.Move(temporary, destination, overwrite: true);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        await Task.CompletedTask;
    }

    private static Task<List<Exception>> RollbackAsync(IReadOnlyList<AppliedFile> applied)
    {
        var errors = new List<Exception>();
        for (var i = applied.Count - 1; i >= 0; i--)
        {
            var file = applied[i];
            try
            {
                if (file.Existed)
                {
                    var temporary = file.Destination + ".firebirdstudio-rollback-" + Guid.NewGuid().ToString("N");
                    File.Copy(file.Backup, temporary, overwrite: false);
                    if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
                        File.SetUnixFileMode(temporary, File.GetUnixFileMode(file.Backup));
                    File.Move(temporary, file.Destination, overwrite: true);
                }
                else if (File.Exists(file.Destination)) File.Delete(file.Destination);
            }
            catch (Exception exception) { errors.Add(exception); }
        }
        return Task.FromResult(errors);
    }

    private void EnsureApplicationInsideInstallDirectory()
    {
        var relative = Path.GetRelativePath(_installDirectory!, _appPath!);
        if (Path.IsPathRooted(relative) || relative.Equals("..", StringComparison.Ordinal) ||
            relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new InvalidDataException("The application executable must be inside the installation directory.");
        if (!File.Exists(_appPath)) throw new FileNotFoundException("The application executable could not be found.", _appPath);
    }

    private static void EnsureNoReparsePoints(string root, string destination)
    {
        var current = Path.GetFullPath(root);
        Check(current);
        var relative = Path.GetRelativePath(current, destination);
        foreach (var segment in relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        {
            if (segment.Length == 0 || segment == ".")
                continue;
            current = Path.Combine(current, segment);
            Check(current);
        }

        static void Check(string path)
        {
            try
            {
                if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException($"The update target contains a symbolic link or reparse point: {path}");
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
    }

    private static void EnsurePathInside(string root, string path)
    {
        var prefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!path.StartsWith(prefix, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new InvalidDataException($"The archive path escapes its target directory: {path}");
    }

    private void TryLaunchApplication()
    {
        try
        {
            if (_appPath is null || _installDirectory is null)
                throw new InvalidOperationException("The application launch path is unavailable.");
            _ = Process.Start(new ProcessStartInfo(_appPath) { UseShellExecute = false, WorkingDirectory = _installDirectory })
                ?? throw new InvalidOperationException("The operating system did not start Firebird Studio.");
            Close();
        }
        catch (Exception exception)
        {
            _status.Text = "Firebird Studio could not be started automatically.";
            _details.Text = exception.Message + Environment.NewLine + "Start the application manually from its installation folder.";
            _launchButton.IsVisible = false;
            _ = LogAsync("Application restart failed: " + exception);
        }
    }

    private void SetStatus(string status, string details, double progress)
    {
        _status.Text = status;
        _details.Text = details;
        _progress.Value = Math.Clamp(progress, 0, 100);
    }

    private async Task LogAsync(string message)
    {
        if (_logPath is null) return;
        try { await File.AppendAllTextAsync(_logPath, $"[{DateTimeOffset.UtcNow:O}] {message}{Environment.NewLine}"); }
        catch { }
    }

    private string Required(string name) =>
        _args.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value : throw new ArgumentException($"Missing required updater argument: {name}");
    private int RequiredInt(string name) =>
        int.TryParse(Required(name), out var value) ? value : throw new ArgumentException($"Invalid integer argument: {name}");
    private long RequiredLong(string name) =>
        long.TryParse(Required(name), out var value) ? value : throw new ArgumentException($"Invalid numeric argument: {name}");

    private static Dictionary<string, string> ParseArguments(string[] args)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < args.Length; i += 2)
        {
            if (i + 1 >= args.Length || !args[i].StartsWith("--", StringComparison.Ordinal))
                throw new ArgumentException("Updater arguments must use --name value pairs.");
            result[args[i]] = args[i + 1];
        }
        return result;
    }

    private sealed record AppliedFile(string Destination, string Backup, bool Existed);
}
