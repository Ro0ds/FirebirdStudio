using System.Globalization;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using FirebirdStudio.Application.Connections;
using FirebirdStudio.Core.Connections;
using FirebirdStudio.Infrastructure.Connections;
using static FirebirdStudio.Desktop.Services.StudioText;

namespace FirebirdStudio.Desktop.Views;

public sealed partial class ConnectionEditorWindow : Window
{
    private readonly ConnectionProfile _originalProfile;
    private readonly ConnectionProfileManager _profileManager;
    private readonly IFirebirdConnectionTester _connectionTester;

    public ConnectionProfile? SavedProfile { get; private set; }

    public ConnectionEditorWindow(
        ConnectionProfile profile,
        ConnectionProfileManager profileManager,
        IFirebirdConnectionTester connectionTester)
    {
        _originalProfile = profile ?? throw new ArgumentNullException(nameof(profile));
        _profileManager = profileManager ?? throw new ArgumentNullException(nameof(profileManager));
        _connectionTester = connectionTester ?? throw new ArgumentNullException(nameof(connectionTester));

        InitializeComponent();
        RuntimeBox.Items.Add(new ComboBoxItem { Content = T("ExternalFirebirdLibrary") });
        if (EmbeddedFirebirdInstaller.IsSupported || profile.EmbeddedEngineVersion is not null)
        {
            foreach (var version in EmbeddedFirebirdInstaller.Versions)
                RuntimeBox.Items.Add(new ComboBoxItem { Content = T("ManagedFirebirdEngine", version), Tag = version });
        }

        RuntimeBox.SelectedItem = RuntimeBox.Items.OfType<ComboBoxItem>()
            .FirstOrDefault(item => Equals(item.Tag, profile.EmbeddedEngineVersion)) ?? RuntimeBox.Items[0];
        PopulateFields(profile);
        RuntimeBox.SelectionChanged += (_, _) => UpdateRuntimeControls();
        this.FindControl<ComboBox>("ServerTypeInput")!.SelectionChanged += (_, _) => UpdateRuntimeControls();
        UpdateRuntimeControls();

        this.FindControl<Button>("TestConnectionButton")!.Click += OnTestConnectionClick;
        this.FindControl<Button>("BrowseDatabaseButton")!.Click += OnBrowseDatabaseClick;
        this.FindControl<Button>("SaveButton")!.Click += OnSaveClick;
        this.FindControl<Button>("CancelButton")!.Click += (_, _) => CloseEditor();
    }

    private string? SelectedEngine => (RuntimeBox.SelectedItem as ComboBoxItem)?.Tag as string;

    private void UpdateRuntimeControls()
    {
        var embedded = this.FindControl<ComboBox>("ServerTypeInput")!.SelectedIndex == (int)ConnectionServerType.Embedded;
        RuntimeSelectPanel.IsVisible = embedded && RuntimeBox.ItemCount > 1;
        ClientLibraryPanel.IsVisible = embedded && SelectedEngine is null;
    }

    private void PopulateFields(ConnectionProfile profile)
    {
        SetText("NameInput", profile.Name);
        SetText("HostInput", profile.Host);
        SetText("PortInput", profile.Port.ToString(CultureInfo.InvariantCulture));
        SetText("DatabaseInput", profile.Database);
        SetText("ClientLibraryInput", profile.ClientLibraryPath ?? string.Empty);
        SetText("UserNameInput", profile.UserName);
        SetText("RoleInput", profile.Role ?? string.Empty);
        SetText("CharacterSetInput", profile.CharacterSet);
        SetText("TimeoutInput", profile.ConnectionTimeoutSeconds.ToString(CultureInfo.InvariantCulture));
        SetText("PacketSizeInput", profile.PacketSize.ToString(CultureInfo.InvariantCulture));
        SetText("FetchSizeInput", profile.FetchSize.ToString(CultureInfo.InvariantCulture));
        SetText("DatabaseCachePagesInput", profile.DatabaseCachePages.ToString(CultureInfo.InvariantCulture));
        SetText("MinPoolSizeInput", profile.MinPoolSize.ToString(CultureInfo.InvariantCulture));
        SetText("MaxPoolSizeInput", profile.MaxPoolSize.ToString(CultureInfo.InvariantCulture));
        SetText("ConnectionLifetimeInput", profile.ConnectionLifetimeSeconds.ToString(CultureInfo.InvariantCulture));

        this.FindControl<ComboBox>("ServerTypeInput")!.SelectedIndex = (int)profile.ServerType;
        this.FindControl<ComboBox>("DialectInput")!.SelectedIndex = profile.SqlDialect - 1;
        SetChecked("PoolingInput", profile.Pooling);
        SetChecked("CompressionInput", profile.Compression);
        SetChecked("DisableDatabaseTriggersInput", profile.DisableDatabaseTriggers);
        SetChecked("DisableGarbageCollectionInput", profile.DisableGarbageCollection);
    }

    private async void OnBrowseDatabaseClick(object? sender, RoutedEventArgs e)
    {
        var storageProvider = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storageProvider is null)
        {
            SetMessage(T("FilePickerUnavailable"));
            return;
        }

        var files = await storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = T("PickFirebirdDatabase"),
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType(T("FirebirdDatabaseFiles"))
                {
                    Patterns = ["*.gdb", "*.GDB", "*.fdb", "*.FDB"]
                }
            ]
        });

        if (files.Count == 0)
            return;

        var selectedPath = files[0].Path.LocalPath;
        var extension = Path.GetExtension(selectedPath);
        if (!string.Equals(extension, ".gdb", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(extension, ".fdb", StringComparison.OrdinalIgnoreCase))
        {
            SetMessage(T("InvalidDatabaseExtension"));
            return;
        }

        SetText("DatabaseInput", selectedPath);
        SetMessage(T("DatabaseSelected", Path.GetFileName(selectedPath)));
    }

    private async void OnTestConnectionClick(object? sender, RoutedEventArgs e)
    {
        var profile = BuildProfile();
        var errors = ConnectionProfileValidator.Validate(profile);
        if (errors.Count > 0)
        {
            SetMessage(string.Join(Environment.NewLine, errors));
            return;
        }

        var password = this.FindControl<TextBox>("PasswordInput")?.Text ?? string.Empty;
        var testButton = this.FindControl<Button>("TestConnectionButton")!;
        testButton.IsEnabled = false;
        SetMessage(T("TestingConnection"));

        try
        {
            var embeddedEngines = ((App)Avalonia.Application.Current!).EmbeddedEngines;
            var resolved = await embeddedEngines.ResolveAsync(profile,
                new Progress<string>(version => SetMessage(T("DownloadingFirebirdEngine", version))));
            var result = await _connectionTester.TestAsync(resolved, password);
            var message = result.Succeeded && !string.IsNullOrWhiteSpace(result.ServerVersion)
                ? result.Message + Environment.NewLine + T("ServerVersion", result.ServerVersion)
                : result.Message;
            if (!string.IsNullOrWhiteSpace(result.Diagnostics))
                message += Environment.NewLine + Environment.NewLine + T("Diagnostics") + Environment.NewLine + result.Diagnostics;
            SetMessage(message);
        }
        catch (Exception exception)
        {
            var details = string.IsNullOrEmpty(password)
                ? exception.ToString()
                : exception.ToString().Replace(password, "[REDACTED]", StringComparison.Ordinal);
            SetMessage(T("ConnectionTestFailed", Environment.NewLine, details));
        }
        finally
        {
            this.FindControl<TextBox>("PasswordInput")!.Text = string.Empty;
            testButton.IsEnabled = true;
        }
    }

    private async void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        var profile = BuildProfile();
        var errors = ConnectionProfileValidator.Validate(profile);
        if (errors.Count > 0)
        {
            SetMessage(string.Join(Environment.NewLine, errors));
            return;
        }

        var saveButton = this.FindControl<Button>("SaveButton")!;
        saveButton.IsEnabled = false;
        try
        {
            await _profileManager.SaveAsync(profile);
            SavedProfile = profile;
            CloseEditor();
        }
        catch (Exception exception)
        {
            SetMessage(T("SaveProfileFailed", Environment.NewLine, exception));
        }
        finally
        {
            saveButton.IsEnabled = true;
        }
    }

    private void CloseEditor()
    {
        var passwordInput = this.FindControl<TextBox>("PasswordInput");
        if (passwordInput is not null)
            passwordInput.Text = string.Empty;

        Close();
    }

    private ConnectionProfile BuildProfile()
    {
        var serverTypeIndex = this.FindControl<ComboBox>("ServerTypeInput")?.SelectedIndex ?? 0;
        var dialectIndex = this.FindControl<ComboBox>("DialectInput")?.SelectedIndex ?? 2;
        var embedded = serverTypeIndex == (int)ConnectionServerType.Embedded;

        return new ConnectionProfile
        {
            Id = _originalProfile.Id,
            Name = GetText("NameInput").Trim(),
            ServerType = embedded
                ? ConnectionServerType.Embedded
                : ConnectionServerType.Remote,
            Host = GetText("HostInput").Trim(),
            Port = ParseInt("PortInput"),
            Database = GetText("DatabaseInput").Trim(),
            ClientLibraryPath = embedded && SelectedEngine is null ? NullIfWhiteSpace(GetText("ClientLibraryInput")) : null,
            EmbeddedEngineVersion = embedded ? SelectedEngine : null,
            UserName = GetText("UserNameInput").Trim(),
            Role = NullIfWhiteSpace(GetText("RoleInput")),
            CharacterSet = GetText("CharacterSetInput").Trim(),
            SqlDialect = dialectIndex is >= 0 and <= 2 ? dialectIndex + 1 : 0,
            ConnectionTimeoutSeconds = ParseInt("TimeoutInput"),
            PacketSize = ParseInt("PacketSizeInput"),
            FetchSize = ParseInt("FetchSizeInput"),
            DatabaseCachePages = ParseInt("DatabaseCachePagesInput"),
            Pooling = IsChecked("PoolingInput"),
            MinPoolSize = ParseInt("MinPoolSizeInput"),
            MaxPoolSize = ParseInt("MaxPoolSizeInput"),
            ConnectionLifetimeSeconds = ParseInt("ConnectionLifetimeInput"),
            Compression = IsChecked("CompressionInput"),
            DisableDatabaseTriggers = IsChecked("DisableDatabaseTriggersInput"),
            DisableGarbageCollection = IsChecked("DisableGarbageCollectionInput")
        };
    }

    private int ParseInt(string name) =>
        int.TryParse(GetText(name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : 0;

    private string GetText(string name) =>
        this.FindControl<TextBox>(name)?.Text ?? string.Empty;

    private void SetText(string name, string value)
    {
        var control = this.FindControl<TextBox>(name);
        if (control is not null)
            control.Text = value;
    }

    private void SetChecked(string name, bool value)
    {
        var control = this.FindControl<CheckBox>(name);
        if (control is not null)
            control.IsChecked = value;
    }

    private bool IsChecked(string name) =>
        this.FindControl<CheckBox>(name)?.IsChecked == true;

    private void SetMessage(string value)
    {
        var control = this.FindControl<TextBlock>("TestResultText");
        if (control is not null)
            control.Text = value;
    }

    private static string? NullIfWhiteSpace(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
