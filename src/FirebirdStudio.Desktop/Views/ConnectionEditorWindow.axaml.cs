using System.Globalization;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using FirebirdStudio.Application.Connections;
using FirebirdStudio.Core.Connections;

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

        AvaloniaXamlLoader.Load(this);
        PopulateFields(profile);

        this.FindControl<Button>("TestConnectionButton")!.Click += OnTestConnectionClick;
        this.FindControl<Button>("BrowseDatabaseButton")!.Click += OnBrowseDatabaseClick;
        this.FindControl<Button>("SaveButton")!.Click += OnSaveClick;
        this.FindControl<Button>("CancelButton")!.Click += (_, _) => CloseEditor();
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
            SetMessage("The file picker is not available on this platform.");
            return;
        }

        var files = await storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select a Firebird database",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Firebird database files")
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
            SetMessage("Please select a Firebird database file with a .GDB or .FDB extension.");
            return;
        }

        SetText("DatabaseInput", selectedPath);
        SetMessage($"Selected database: {Path.GetFileName(selectedPath)}");
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
        SetMessage("Testing connection...");

        try
        {
            var result = await _connectionTester.TestAsync(profile, password);
            var message = result.Succeeded && !string.IsNullOrWhiteSpace(result.ServerVersion)
                ? $"{result.Message}{Environment.NewLine}Server version: {result.ServerVersion}"
                : result.Message;
            if (!string.IsNullOrWhiteSpace(result.Diagnostics))
                message += $"{Environment.NewLine}{Environment.NewLine}Diagnostics:{Environment.NewLine}{result.Diagnostics}";
            SetMessage(message);
        }
        catch (Exception exception)
        {
            var details = string.IsNullOrEmpty(password)
                ? exception.ToString()
                : exception.ToString().Replace(password, "[REDACTED]", StringComparison.Ordinal);
            SetMessage($"Connection test could not complete:{Environment.NewLine}{details}");
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
            SetMessage($"Could not save profile:{Environment.NewLine}{exception}");
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

        return new ConnectionProfile
        {
            Id = _originalProfile.Id,
            Name = GetText("NameInput").Trim(),
            ServerType = serverTypeIndex == (int)ConnectionServerType.Embedded
                ? ConnectionServerType.Embedded
                : ConnectionServerType.Remote,
            Host = GetText("HostInput").Trim(),
            Port = ParseInt("PortInput"),
            Database = GetText("DatabaseInput").Trim(),
            ClientLibraryPath = NullIfWhiteSpace(GetText("ClientLibraryInput")),
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
