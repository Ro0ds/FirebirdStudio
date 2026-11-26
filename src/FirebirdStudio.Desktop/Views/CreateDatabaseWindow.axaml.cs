using Avalonia.Controls;
using Avalonia.Platform.Storage;
using FirebirdStudio.Core.Connections;
using FirebirdStudio.Desktop.Services;
using FirebirdStudio.Infrastructure.Connections;
using static FirebirdStudio.Desktop.Services.StudioText;

namespace FirebirdStudio.Desktop.Views;

internal sealed record CreatedDatabase(string Path, ConnectionProfile? NewProfile);

internal sealed partial class CreateDatabaseWindow : Window
{
    private readonly EmbeddedFirebirdInstaller _engines;
    private readonly ConnectionProfile? _active;
    private string? _activePassword;
    private bool _creating;
    private CancellationTokenSource? _download;

    public CreateDatabaseWindow(ConnectionProfile? active, string? version, string? password, EmbeddedFirebirdInstaller engines)
    {
        _active = active;
        _activePassword = password;
        _engines = engines;
        InitializeComponent();

        var embeddedAvailable = EmbeddedFirebirdInstaller.IsSupported;
        ActiveInstanceOption.IsEnabled = active is not null;
        ActiveInstanceDescription.Text = active is null
            ? T("DatabaseCreationConnectFirst")
            : T("DatabaseCreationTarget", active.Name, version);
        EmbeddedInstanceOption.IsVisible = embeddedAvailable;
        if (embeddedAvailable)
        {
            foreach (var engineVersion in EmbeddedFirebirdInstaller.Versions)
                EmbeddedEngineBox.Items.Add(new ComboBoxItem
                {
                    Content = T("ManagedFirebirdEngine", engineVersion),
                    Tag = new ConnectionProfile { ServerType = ConnectionServerType.Embedded, EmbeddedEngineVersion = engineVersion }
                });
            EmbeddedEngineBox.SelectedIndex = 0;
        }

        ActiveInstanceOption.IsChecked = active is not null;
        EmbeddedInstanceOption.IsChecked = active is null && embeddedAvailable;
        ActiveInstanceOption.IsCheckedChanged += (_, _) => UpdateTarget();
        EmbeddedInstanceOption.IsCheckedChanged += (_, _) => UpdateTarget();
        UpdateTarget();
        BrowseButton.Click += async (_, _) => await BrowseAsync();
        CreateButton.Click += async (_, _) => await CreateAsync();
        CancelButton.Click += (_, _) =>
        {
            if (_download is { } download)
                download.Cancel();
            else
                Close();
        };
        Closing += (_, args) =>
        {
            args.Cancel = _creating;
            _download?.Cancel();
        };
        Closed += (_, _) =>
        {
            PasswordBox.Text = null;
            _activePassword = null;
        };
    }

    private ConnectionProfile? Target => ActiveInstanceOption.IsChecked == true
        ? _active
        : EmbeddedInstanceOption.IsChecked == true
            ? (EmbeddedEngineBox.SelectedItem as ComboBoxItem)?.Tag as ConnectionProfile
            : null;

    private void UpdateTarget()
    {
        var target = Target;
        var local = target?.ServerType == ConnectionServerType.Embedded;
        EmbeddedEnginePanel.IsVisible = EmbeddedInstanceOption.IsChecked == true;
        PathLabel.Text = T(local ? "DatabaseCreationLocalPath" : "DatabaseCreationServerPath");
        BrowseButton.IsVisible = local;
        PasswordPanel.IsVisible = target is { EmbeddedEngineVersion: null };
        PasswordBox.Text = ActiveInstanceOption.IsChecked == true ? _activePassword : null;
        CreateButton.IsEnabled = target is not null;
        StatusText.IsVisible = false;
    }

    private async Task BrowseAsync()
    {
        try
        {
            var target = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = T("CreateDatabase"),
                DefaultExtension = "fdb",
                FileTypeChoices = [FirebirdDatabaseFiles.CreateFilter()]
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
        if (_creating || Target is not { } source)
            return;

        var path = PathBox.Text?.Trim() ?? string.Empty;
        if (path.Length == 0 || (source.ServerType == ConnectionServerType.Embedded && !System.IO.Path.IsPathFullyQualified(path)))
        {
            SetStatus(T(source.ServerType == ConnectionServerType.Embedded ? "DatabaseCreationLocalPathRequired" : "DatabaseCreationPathRequired"), error: true);
            return;
        }

        var register = RegisterProfileBox.IsChecked == true;
        var name = NameBox.Text?.Trim();
        if (register && string.IsNullOrWhiteSpace(name))
        {
            SetStatus(T("DatabaseCreationProfileNameRequired"), error: true);
            return;
        }

        var target = source with { Id = Guid.NewGuid(), Database = path, Name = register ? name! : path };
        var password = PasswordBox.Text ?? string.Empty;
        var errors = ConnectionProfileValidator.Validate(target);
        if (errors.Count > 0)
        {
            SetStatus(string.Join(Environment.NewLine, errors), error: true);
            return;
        }

        _creating = true;
        CreateButton.IsEnabled = SourceOptions.IsEnabled = false;
        SetStatus(T("DatabaseCreating"), error: false);

        using var download = new CancellationTokenSource();
        _download = download;
        try
        {
            var resolved = await _engines.ResolveAsync(target, new Progress<string>(version =>
            {
                if (_download is not null)
                    SetStatus(T("DownloadingFirebirdEngine", version), error: false);
            }), download.Token);
            download.Token.ThrowIfCancellationRequested();
            _download = null;
            CancelButton.IsEnabled = false;
            SetStatus(T("DatabaseCreating"), error: false);
            await FirebirdDatabaseCreator.CreateAsync(resolved, password);
        }
        catch (OperationCanceledException) when (download.IsCancellationRequested)
        {
            SetStatus(T("FirebirdDownloadCancelled"), error: false);
            return;
        }
        catch (Exception exception)
        {
            var tlsFailure = exception is HttpRequestException { HttpRequestError: HttpRequestError.SecureConnectionError };
            var detail = tlsFailure ? exception.GetBaseException().Message : exception.Message;
            if (password.Length > 0)
                detail = detail.Replace(password, "[REDACTED]", StringComparison.Ordinal);
            SetStatus(T(tlsFailure ? "FirebirdDownloadTlsFailed" : "DatabaseCreationFailed", detail), error: true);
            return;
        }
        finally
        {
            _creating = false;
            _download = null;
            CreateButton.IsEnabled = CancelButton.IsEnabled = SourceOptions.IsEnabled = true;
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
