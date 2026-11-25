using System.Globalization;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;

namespace FirebirdStudio.Desktop.Services;

internal sealed record ThemeColorOverrides(
    string? Accent,
    string? SelectionBackground,
    string? SelectionForeground);

internal readonly record struct StudioPalette(
    Color Surface,
    Color Accent,
    Color AccentForeground,
    Color SelectionBackground,
    Color SelectionForeground);

internal sealed class StudioAppearance
{
    public const double MinimumTextContrast = 4.5;

    private readonly IResourceDictionary _resources;
    private readonly StudioPalette _lightDefaults;
    private readonly StudioPalette _darkDefaults;
    private ResourceDictionary? _activeOverrides;

    public StudioAppearance(IResourceDictionary resources)
    {
        _resources = resources;
        _lightDefaults = ReadDefaults(ThemeVariant.Light);
        _darkDefaults = ReadDefaults(ThemeVariant.Dark);
    }

    public StudioPalette Defaults(string theme) => theme == "Dark" ? _darkDefaults : _lightDefaults;

    public StudioPalette Resolve(string theme, ThemeColorOverrides? overrides)
    {
        var defaults = Defaults(theme);
        var accent = ParseOrDefault(overrides?.Accent, defaults.Accent);
        var background = ParseOrDefault(overrides?.SelectionBackground, defaults.SelectionBackground);
        var foreground = ParseOrDefault(overrides?.SelectionForeground, defaults.SelectionForeground);
        if (Contrast(accent, defaults.Surface) < MinimumTextContrast)
            accent = defaults.Accent;
        if (Contrast(background, foreground) < MinimumTextContrast)
        {
            background = defaults.SelectionBackground;
            foreground = defaults.SelectionForeground;
        }

        return defaults with
        {
            Accent = accent,
            AccentForeground = GetAccentForeground(defaults, accent),
            SelectionBackground = background,
            SelectionForeground = foreground
        };
    }

    public ThemeColorOverrides? CreateOverrides(string theme, StudioPalette palette)
    {
        var defaults = Defaults(theme);
        var accent = palette.Accent == defaults.Accent ? null : ToHex(palette.Accent);
        var background = palette.SelectionBackground == defaults.SelectionBackground ? null : ToHex(palette.SelectionBackground);
        var foreground = palette.SelectionForeground == defaults.SelectionForeground ? null : ToHex(palette.SelectionForeground);
        return accent is null && background is null && foreground is null
            ? null
            : new ThemeColorOverrides(accent, background, foreground);
    }

    public void Apply(string theme, ThemeColorOverrides? overrides)
    {
        var defaults = Defaults(theme);
        var palette = Resolve(theme, overrides);
        var resources = new ResourceDictionary();
        if (palette.Accent != defaults.Accent)
        {
            var accentBrush = new SolidColorBrush(palette.Accent);
            var foregroundBrush = new SolidColorBrush(palette.AccentForeground);
            var tint = palette.AccentForeground == Colors.White ? Colors.Black : Colors.White;
            resources["StudioAccentBrush"] = accentBrush;
            resources["AccentButtonBackground"] = accentBrush;
            resources["AccentButtonBackgroundPointerOver"] = new SolidColorBrush(Blend(palette.Accent, tint, 0.12));
            resources["AccentButtonBackgroundPressed"] = new SolidColorBrush(Blend(palette.Accent, tint, 0.24));
            resources["AccentButtonForeground"] = foregroundBrush;
            resources["AccentButtonForegroundPointerOver"] = foregroundBrush;
            resources["AccentButtonForegroundPressed"] = foregroundBrush;
            resources["SqlSelectionBorder"] = new Pen(accentBrush, 1);
        }
        if (palette.SelectionBackground != defaults.SelectionBackground)
            resources["SqlSelectionBackground"] = new SolidColorBrush(palette.SelectionBackground);
        if (palette.SelectionForeground != defaults.SelectionForeground)
            resources["SqlSelectionForeground"] = new SolidColorBrush(palette.SelectionForeground);

        var mergedDictionaries = _resources.MergedDictionaries;
        if (_activeOverrides is not null)
            mergedDictionaries.Remove(_activeOverrides);
        _activeOverrides = resources.Count > 0 ? resources : null;
        if (_activeOverrides is not null)
            mergedDictionaries.Add(_activeOverrides);
    }

    public static Color GetAccentForeground(StudioPalette defaults, Color accent) =>
        accent == defaults.Accent
            ? defaults.AccentForeground
            : Contrast(accent, Colors.White) >= Contrast(accent, Colors.Black) ? Colors.White : Colors.Black;

    public static bool TryParseHex(string? value, out Color color)
    {
        var hex = value.AsSpan().Trim();
        if (hex.Length == 7 && hex[0] == '#' &&
            uint.TryParse(hex[1..], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var rgb))
        {
            color = Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
            return true;
        }

        color = default;
        return false;
    }

    public static string ToHex(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";

    public static double Contrast(Color first, Color second)
    {
        var light = Luminance(first);
        var dark = Luminance(second);
        return (Math.Max(light, dark) + 0.05) / (Math.Min(light, dark) + 0.05);
    }

    private StudioPalette ReadDefaults(ThemeVariant theme) => new(
        ReadColor("StudioSurfaceBrush", theme),
        ReadColor("StudioAccentBrush", theme),
        ReadColor("AccentButtonForeground", theme),
        ReadColor("SqlSelectionBackground", theme),
        ReadColor("SqlSelectionForeground", theme));

    private Color ReadColor(string key, ThemeVariant theme)
    {
        if (_resources.TryGetResource(key, theme, out var resource) && resource is ISolidColorBrush brush)
            return brush.Color;
        throw new InvalidOperationException($"The theme resource '{key}' is unavailable.");
    }

    private static Color ParseOrDefault(string? value, Color fallback) =>
        TryParseHex(value, out var color) ? color : fallback;

    private static double Luminance(Color color) =>
        0.2126 * LinearChannel(color.R) + 0.7152 * LinearChannel(color.G) + 0.0722 * LinearChannel(color.B);

    private static double LinearChannel(byte value)
    {
        var normalized = value / 255.0;
        return normalized <= 0.04045 ? normalized / 12.92 : Math.Pow((normalized + 0.055) / 1.055, 2.4);
    }

    private static Color Blend(Color source, Color tint, double amount)
    {
        byte BlendChannel(byte from, byte to) => (byte)Math.Round(from + (to - from) * amount);
        return Color.FromRgb(BlendChannel(source.R, tint.R), BlendChannel(source.G, tint.G), BlendChannel(source.B, tint.B));
    }
}
