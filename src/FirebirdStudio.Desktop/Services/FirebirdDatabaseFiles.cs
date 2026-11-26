using Avalonia.Platform.Storage;

namespace FirebirdStudio.Desktop.Services;

internal static class FirebirdDatabaseFiles
{
    private static readonly string[] Extensions = [".fdb", ".gdb"];

    public static FilePickerFileType CreateFilter() => new(StudioText.T("FirebirdDatabaseFiles"))
    {
        Patterns = Extensions.SelectMany(extension => new[] { $"*{extension}", $"*{extension.ToUpperInvariant()}" }).ToArray()
    };

    public static bool HasSupportedExtension(string path) =>
        Extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);
}
