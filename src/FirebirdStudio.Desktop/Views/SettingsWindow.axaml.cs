using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using FirebirdStudio.Desktop.Services;
using FirebirdStudio.Infrastructure.Connections;
using static FirebirdStudio.Desktop.Services.StudioText;

namespace FirebirdStudio.Desktop.Views;

internal sealed record SettingsChanges(Key ExecuteShortcut, string InterfaceLanguage, ThemeColorOverrides? Light,
    ThemeColorOverrides? Dark, bool DownloadAllEmbeddedEngines);

internal sealed partial class SettingsWindow : Window
{
    private readonly StudioAppearance _appearance;
    private ThemeColorOverrides _lightDraft;
    private ThemeColorOverrides _darkDraft;
    private string _editedTheme;
    private bool _updatingColorPickers;
    private CancellationTokenSource? _engineDownload;

    public SettingsWindow(StudioPreferences preferences, StudioAppearance appearance)
    {
        _appearance = appearance;
        _lightDraft = DisplayValues(appearance.Resolve("Light", preferences.LightThemeColors));
        _darkDraft = DisplayValues(appearance.Resolve("Dark", preferences.DarkThemeColors));
        _editedTheme = preferences.Theme == "Dark" ? "Dark" : "Light";
        InitializeComponent();

        EmbeddedEnginesSettingsTab.IsVisible = EmbeddedFirebirdInstaller.IsSupported;
        EmbeddedDownloadBox.SelectedIndex = preferences.DownloadAllEmbeddedEngines ? 1 : 0;
        InterfaceLanguageBox.SelectedIndex = preferences.InterfaceLanguage == StudioText.Portuguese ? 1 : 0;
        ExecuteSqlShortcutBox.ItemsSource = SqlExecutionShortcuts.Options;
        ExecuteSqlShortcutBox.SelectedItem = preferences.ExecuteSqlKey;
        PaletteThemeBox.SelectedIndex = _editedTheme == "Dark" ? 1 : 0;

        ResetShortcutButton.Click += (_, _) => ExecuteSqlShortcutBox.SelectedItem = SqlExecutionShortcuts.Default;
        CancelSettingsButton.Click += (_, _) => Close();
        SaveSettingsButton.Click += async (_, _) => await SaveAsync();
        Closed += (_, _) => _engineDownload?.Cancel();
        PaletteThemeBox.SelectionChanged += (_, _) => ChangeEditedTheme();
        ResetThemeColorsButton.Click += (_, _) => ShowThemeDraft(DisplayValues(_appearance.Defaults(_editedTheme)));
        ConnectColorField(AccentColorBox, AccentColorPicker);
        ConnectColorField(SelectionBackgroundBox, SelectionBackgroundPicker);
        ConnectColorField(SelectionForegroundBox, SelectionForegroundPicker);

        ShowThemeDraft(_editedTheme == "Dark" ? _darkDraft : _lightDraft);
    }

    private void ConnectColorField(TextBox field, ColorPicker picker)
    {
        picker.SelectedIndex = (int)ColorViewTab.Palette;
        field.TextChanged += (_, _) => UpdatePalettePreview();
        picker.ColorChanged += (_, args) =>
        {
            if (!_updatingColorPickers)
                field.Text = StudioAppearance.ToHex(args.NewColor);
        };
    }

    private void ChangeEditedTheme()
    {
        if (PaletteThemeBox.SelectedItem is not ComboBoxItem { Tag: string theme } || theme == _editedTheme)
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

        _updatingColorPickers = true;
        try
        {
            AccentColorPicker.Color = palette.Accent;
            SelectionBackgroundPicker.Color = palette.SelectionBackground;
            SelectionForegroundPicker.Color = palette.SelectionForeground;
        }
        finally
        {
            _updatingColorPickers = false;
        }

        PalettePreviewSurface.Background = new SolidColorBrush(palette.Surface);
        var previewText = new SolidColorBrush(_editedTheme == "Dark" ? Colors.White : Colors.Black);
        PalettePreviewTitle.Foreground = previewText;
        SelectionContrastText.Foreground = previewText;
        AccentPreview.Background = new SolidColorBrush(palette.Accent);
        AccentPreviewText.Foreground = new SolidColorBrush(palette.AccentForeground);
        SelectionPreview.Background = new SolidColorBrush(palette.SelectionBackground);
        SelectionPreview.BorderBrush = new SolidColorBrush(palette.Accent);
        SelectionPreviewText.Foreground = new SolidColorBrush(palette.SelectionForeground);
        SelectionContrastText.Text = T("SelectionContrast", StudioAppearance.Contrast(palette.SelectionBackground, palette.SelectionForeground));

        PaletteValidationText.Text = error;
        PaletteValidationText.IsVisible = error is not null;
        SaveSettingsButton.IsEnabled = error is null && _engineDownload is null;
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

        var themeLabel = ThemeName(theme).ToLower(Culture);
        if (!accentParsed || !backgroundParsed || !foregroundParsed)
            return (palette, T("PaletteInvalidHex", themeLabel));
        if (StudioAppearance.Contrast(accent, defaults.Surface) < StudioAppearance.MinimumTextContrast)
            return (palette, T("PaletteInvalidAccentContrast", themeLabel));
        if (StudioAppearance.Contrast(background, foreground) < StudioAppearance.MinimumTextContrast)
            return (palette, T("PaletteInvalidSelectionContrast", themeLabel));
        return (palette, null);
    }

    private async Task SaveAsync()
    {
        StoreThemeDraft();
        var light = Evaluate("Light", _lightDraft);
        var dark = Evaluate("Dark", _darkDraft);
        if (_engineDownload is not null || light.Error is not null || dark.Error is not null || ExecuteSqlShortcutBox.SelectedItem is not Key shortcut ||
            InterfaceLanguageBox.SelectedItem is not ComboBoxItem { Tag: string language })
            return;

        var downloadAll = EmbeddedDownloadBox.SelectedIndex == 1;
        if (downloadAll && EmbeddedFirebirdInstaller.IsSupported && !await DownloadEnginesAsync())
            return;

        Close(new SettingsChanges(shortcut, language, _appearance.CreateOverrides("Light", light.Palette),
            _appearance.CreateOverrides("Dark", dark.Palette), downloadAll));
    }

    private async Task<bool> DownloadEnginesAsync()
    {
        using var cancellation = new CancellationTokenSource();
        _engineDownload = cancellation;
        SettingsTabs.SelectedItem = EmbeddedEnginesSettingsTab;
        SettingsTabs.IsEnabled = SaveSettingsButton.IsEnabled = false;
        EmbeddedDownloadStatus.IsVisible = true;
        EmbeddedDownloadStatus.Classes.Set("error", false);
        EmbeddedDownloadStatus.Text = T("CheckingFirebirdEngines");
        try
        {
            var installer = ((App)Avalonia.Application.Current!).EmbeddedEngines;
            await installer.DownloadAllAsync(new Progress<string>(version =>
            {
                if (_engineDownload is not null)
                    EmbeddedDownloadStatus.Text = T("DownloadingFirebirdEngine", version);
            }), cancellation.Token);
            return !cancellation.IsCancellationRequested;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception exception)
        {
            EmbeddedDownloadStatus.Text = exception is HttpRequestException { HttpRequestError: HttpRequestError.SecureConnectionError }
                ? T("FirebirdDownloadTlsFailed", exception.GetBaseException().Message)
                : T("FirebirdDownloadFailed", exception.Message);
            EmbeddedDownloadStatus.Classes.Set("error", true);
            return false;
        }
        finally
        {
            _engineDownload = null;
            SettingsTabs.IsEnabled = true;
            UpdatePalettePreview();
        }
    }

    private static ThemeColorOverrides DisplayValues(StudioPalette palette) => new(
        StudioAppearance.ToHex(palette.Accent), StudioAppearance.ToHex(palette.SelectionBackground),
        StudioAppearance.ToHex(palette.SelectionForeground));
}
