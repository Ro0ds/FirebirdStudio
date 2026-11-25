using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using FirebirdStudio.Desktop.Services;

namespace FirebirdStudio.Desktop.Views;

internal sealed record SettingsChanges(Key ExecuteShortcut, ThemeColorOverrides? Light, ThemeColorOverrides? Dark);

internal sealed partial class SettingsWindow : Window
{
    private readonly StudioAppearance _appearance;
    private ThemeColorOverrides _lightDraft;
    private ThemeColorOverrides _darkDraft;
    private string _editedTheme;

    public SettingsWindow(StudioPreferences preferences, StudioAppearance appearance)
    {
        _appearance = appearance;
        _lightDraft = DisplayValues(appearance.Resolve("Light", preferences.LightThemeColors));
        _darkDraft = DisplayValues(appearance.Resolve("Dark", preferences.DarkThemeColors));
        _editedTheme = preferences.Theme == "Dark" ? "Dark" : "Light";
        InitializeComponent();

        ExecuteSqlShortcutBox.ItemsSource = SqlExecutionShortcuts.Options;
        ExecuteSqlShortcutBox.SelectedItem = preferences.ExecuteSqlKey;
        PaletteThemeBox.ItemsSource = new[] { "Light", "Dark" };
        PaletteThemeBox.SelectedItem = _editedTheme;

        ResetShortcutButton.Click += (_, _) => ExecuteSqlShortcutBox.SelectedItem = SqlExecutionShortcuts.Default;
        CancelSettingsButton.Click += (_, _) => Close();
        SaveSettingsButton.Click += (_, _) => Save();
        PaletteThemeBox.SelectionChanged += (_, _) => ChangeEditedTheme();
        ResetThemeColorsButton.Click += (_, _) => ShowThemeDraft(DisplayValues(_appearance.Defaults(_editedTheme)));
        AccentColorBox.TextChanged += (_, _) => UpdatePalettePreview();
        SelectionBackgroundBox.TextChanged += (_, _) => UpdatePalettePreview();
        SelectionForegroundBox.TextChanged += (_, _) => UpdatePalettePreview();

        ShowThemeDraft(_editedTheme == "Dark" ? _darkDraft : _lightDraft);
    }

    private void ChangeEditedTheme()
    {
        if (PaletteThemeBox.SelectedItem is not string theme || theme == _editedTheme)
            return;
        StoreThemeDraft();
        _editedTheme = theme;
        ShowThemeDraft(theme == "Dark" ? _darkDraft : _lightDraft);
    }

    private void ShowThemeDraft(ThemeColorOverrides draft)
    {
        AccentColorBox.Text = draft.Accent;
        SelectionBackgroundBox.Text = draft.SelectionBackground;
        SelectionForegroundBox.Text = draft.SelectionForeground;
        UpdatePalettePreview();
    }

    private ThemeColorOverrides ReadColorFields() =>
        new(AccentColorBox.Text, SelectionBackgroundBox.Text, SelectionForegroundBox.Text);

    private void StoreThemeDraft()
    {
        if (_editedTheme == "Dark")
            _darkDraft = ReadColorFields();
        else
            _lightDraft = ReadColorFields();
    }

    private void UpdatePalettePreview()
    {
        var (palette, currentError) = Evaluate(_editedTheme, ReadColorFields());
        var (otherTheme, otherDraft) = _editedTheme == "Dark" ? ("Light", _lightDraft) : ("Dark", _darkDraft);
        var error = currentError ?? Evaluate(otherTheme, otherDraft).Error;

        AccentColorSwatch.Background = new SolidColorBrush(palette.Accent);
        SelectionBackgroundSwatch.Background = new SolidColorBrush(palette.SelectionBackground);
        SelectionForegroundSwatch.Background = new SolidColorBrush(palette.SelectionForeground);
        PalettePreviewSurface.Background = new SolidColorBrush(palette.Surface);
        var previewText = new SolidColorBrush(_editedTheme == "Dark" ? Colors.White : Colors.Black);
        PalettePreviewTitle.Foreground = previewText;
        SelectionContrastText.Foreground = previewText;
        AccentPreview.Background = new SolidColorBrush(palette.Accent);
        AccentPreviewText.Foreground = new SolidColorBrush(palette.AccentForeground);
        SelectionPreview.Background = new SolidColorBrush(palette.SelectionBackground);
        SelectionPreview.BorderBrush = new SolidColorBrush(palette.Accent);
        SelectionPreviewText.Foreground = new SolidColorBrush(palette.SelectionForeground);
        SelectionContrastText.Text = $"SQL selection contrast {StudioAppearance.Contrast(palette.SelectionBackground, palette.SelectionForeground):F2}:1";

        PaletteValidationText.Text = error;
        PaletteValidationText.IsVisible = error is not null;
        SaveSettingsButton.IsEnabled = error is null;
    }

    private (StudioPalette Palette, string? Error) Evaluate(string theme, ThemeColorOverrides draft)
    {
        var defaults = _appearance.Defaults(theme);
        var accentParsed = StudioAppearance.TryParseHex(draft.Accent, out var accent);
        var backgroundParsed = StudioAppearance.TryParseHex(draft.SelectionBackground, out var background);
        var foregroundParsed = StudioAppearance.TryParseHex(draft.SelectionForeground, out var foreground);
        var palette = defaults with
        {
            Accent = accentParsed ? accent : defaults.Accent,
            AccentForeground = accentParsed ? StudioAppearance.GetAccentForeground(defaults, accent) : defaults.AccentForeground,
            SelectionBackground = backgroundParsed ? background : defaults.SelectionBackground,
            SelectionForeground = foregroundParsed ? foreground : defaults.SelectionForeground
        };

        if (!accentParsed || !backgroundParsed || !foregroundParsed)
            return (palette, $"Use #RRGGBB for all three {theme.ToLowerInvariant()} theme colors.");
        if (StudioAppearance.Contrast(accent, defaults.Surface) < StudioAppearance.MinimumTextContrast)
            return (palette, $"The {theme.ToLowerInvariant()} theme accent needs at least 4.5:1 contrast against its surface.");
        if (StudioAppearance.Contrast(background, foreground) < StudioAppearance.MinimumTextContrast)
            return (palette, $"The {theme.ToLowerInvariant()} theme SQL selection text needs at least 4.5:1 contrast against its background.");
        return (palette, null);
    }

    private void Save()
    {
        StoreThemeDraft();
        var light = Evaluate("Light", _lightDraft);
        var dark = Evaluate("Dark", _darkDraft);
        if (light.Error is not null || dark.Error is not null || ExecuteSqlShortcutBox.SelectedItem is not Key shortcut)
            return;

        Close(new SettingsChanges(shortcut, _appearance.CreateOverrides("Light", light.Palette),
            _appearance.CreateOverrides("Dark", dark.Palette)));
    }

    private static ThemeColorOverrides DisplayValues(StudioPalette palette) => new(
        StudioAppearance.ToHex(palette.Accent), StudioAppearance.ToHex(palette.SelectionBackground),
        StudioAppearance.ToHex(palette.SelectionForeground));
}
