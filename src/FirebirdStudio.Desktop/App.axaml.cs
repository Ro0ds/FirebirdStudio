using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using FirebirdStudio.Desktop.Services;
using FirebirdStudio.Infrastructure.Connections;

namespace FirebirdStudio.Desktop;

public sealed partial class App : Avalonia.Application
{
    private StudioAppearance? _appearance;

    internal StudioAppearance Appearance => _appearance ??= new(Resources);
    internal EmbeddedFirebirdInstaller EmbeddedEngines { get; } = new();

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var store = new StudioPreferencesStore();
            var preferences = store.LoadAsync();
            try
            {
                StudioText.SelectLanguage(preferences.GetAwaiter().GetResult().InterfaceLanguage);
            }
            catch (Exception)
            {
                // The main window reports the same load failure when it opens.
                StudioText.SelectLanguage(StudioText.English);
            }

            desktop.MainWindow = new Views.MainWindow(store, preferences);
        }

        base.OnFrameworkInitializationCompleted();
    }
}
