using Avalonia.Controls;
using Avalonia.Platform.Storage;
using FirebirdStudio.Core.Connections;
using FirebirdStudio.Infrastructure.Connections;
using static FirebirdStudio.Desktop.Services.StudioText;

namespace FirebirdStudio.Desktop.Views;

internal sealed record CreatedDatabase(string Path, ConnectionProfile? NewProfile);

internal sealed partial class CreateDatabaseWindow : Window
{
    private readonly ConnectionProfile _source;
    private bool _creating;

    public CreateDatabaseWindow(ConnectionProfile source, string version, string? password)
    {
        _source = source;
        InitializeComponent();

        TargetDescription.Text = T("DatabaseCreationTarget", source.Name, version);
        PathLabel.Text = T(source.ServerType == ConnectionServerType.Remote ? "DatabaseCreationServerPath" : "DatabaseCreationLocalPath");
        BrowseButton.IsVisible = source.ServerType == ConnectionServerType.Embedded;
        PasswordBox.Text = password;
        BrowseButton.Click += async (_, _) => await BrowseAsync();
        CreateButton.Click += async (_, _) => await CreateAsync();
        CancelButton.Click += (_, _) => Close();
        Closing += (_, args) => args.Cancel = _creating;
        Closed += (_, _) => PasswordBox.Text = null;
    }

    private async Task BrowseAsync()
    {
        try
        {
            var target = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = T("CreateDatabase"),
                DefaultExtension = "fdb",
                FileTypeChoices = [new FilePickerFileType(T("FirebirdDatabaseFiles")) { Patterns = ["*.fdb"] }]
            });
            if (target?.TryGetLocalPath() is { } path)
                PathBox.Text = path;
        }
        catch (Exception exception)
        {
            SetStatus(T("DatabaseCreationFailed", exception.Message), error: true);
        }
    }

    private async Task CreateAsync()
    {
        if (_creating)
            return;

        var path = PathBox.Text?.Trim() ?? string.Empty;
        if (path.Length == 0 || (_source.ServerType == ConnectionServerType.Embedded && !System.IO.Path.IsPathFullyQualified(path)))
        {
            SetStatus(T(_source.ServerType == ConnectionServerType.Embedded ? "DatabaseCreationLocalPathRequired" : "DatabaseCreationPathRequired"), error: true);
            return;
        }

        var register = RegisterProfileBox.IsChecked == true;
        var name = NameBox.Text?.Trim();
        if (register && string.IsNullOrWhiteSpace(name))
        {
            SetStatus(T("DatabaseCreationProfileNameRequired"), error: true);
            return;
        }

        var target = _source with { Id = Guid.NewGuid(), Database = path, Name = register ? name! : _source.Name };
        var password = PasswordBox.Text ?? string.Empty;
        _creating = true;
        CreateButton.IsEnabled = CancelButton.IsEnabled = false;
        SetStatus(T("DatabaseCreating"), error: false);

        try
        {
            await FirebirdDatabaseCreator.CreateAsync(target, password);
        }
        catch (Exception exception)
        {
            var detail = password.Length == 0 ? exception.Message : exception.Message.Replace(password, "[REDACTED]", StringComparison.Ordinal);
            SetStatus(T("DatabaseCreationFailed", detail), error: true);
            return;
        }
        finally
        {
            _creating = false;
            CreateButton.IsEnabled = CancelButton.IsEnabled = true;
            PasswordBox.Text = null;
        }

        Close(new CreatedDatabase(path, register ? target : null));
    }

    private void SetStatus(string message, bool error)
    {
        StatusText.Text = message;
        StatusText.IsVisible = true;
        StatusText.Classes.Set("error", error);
    }
}
