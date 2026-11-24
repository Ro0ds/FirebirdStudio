using Avalonia.Input;

namespace FirebirdStudio.Desktop.Services;

internal static class SqlExecutionShortcuts
{
    public const Key Default = Key.F9;

    public static IReadOnlyList<Key> Options { get; } =
    [
        Key.F1, Key.F2, Key.F3, Key.F4, Key.F5, Key.F6,
        Key.F7, Key.F8, Key.F9, Key.F11, Key.F12
    ];

    public static Key Resolve(string? savedName) =>
        Options.FirstOrDefault(key => string.Equals(key.ToString(), savedName, StringComparison.OrdinalIgnoreCase), Default);
}
