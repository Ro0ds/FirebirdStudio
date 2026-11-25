using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using FirebirdStudio.Desktop.Services;

namespace FirebirdStudio.Desktop;

public sealed partial class App : Avalonia.Application
{
    private StudioAppearance? _appearance;

    internal StudioAppearance Appearance => _appearance ??= new(Resources);

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new Views.MainWindow();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
