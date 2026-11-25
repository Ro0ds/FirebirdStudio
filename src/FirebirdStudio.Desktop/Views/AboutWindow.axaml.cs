using System.Reflection;
using Avalonia.Controls;
using FirebirdSql.Data.FirebirdClient;

namespace FirebirdStudio.Desktop.Views;

internal sealed partial class AboutWindow : Window
{
    public AboutWindow(Version applicationVersion)
    {
        InitializeComponent();
        ApplicationVersionText.Text = $"Firebird Studio {applicationVersion}";
        ProviderVersionText.Text = GetVersion(typeof(FbConnection).Assembly);
        AvaloniaVersionText.Text = GetVersion(typeof(Window).Assembly);
        RuntimeVersionText.Text = Environment.Version.ToString();
        CloseAboutButton.Click += (_, _) => Close();
    }

    private static string GetVersion(Assembly assembly) =>
        assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
        ?? assembly.GetName().Version?.ToString()
        ?? "Unknown";
}
