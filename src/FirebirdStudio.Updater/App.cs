using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;

namespace FirebirdStudio.Updater;

internal sealed class App : Application
{
    public static string[] Arguments { get; set; } = [];

    public override void Initialize() => Styles.Add(new FluentTheme());

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = new MainWindow(Arguments);
        base.OnFrameworkInitializationCompleted();
    }
}
