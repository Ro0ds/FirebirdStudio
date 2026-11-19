using System.Text.RegularExpressions;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Styling;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Avalonia.Threading;
using Avalonia.Input;
using Avalonia.Media;
using AvaloniaEdit.CodeCompletion;
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;
using Avalonia.Markup.Xaml;
using FirebirdStudio.Application.Connections;
using FirebirdStudio.Application.Sql;
using FirebirdStudio.Core.Connections;
using FirebirdStudio.Core.Database;
using FirebirdStudio.Infrastructure.Connections;
using FirebirdStudio.Desktop.Services;
using FirebirdStudio.Desktop.Sql;

namespace FirebirdStudio.Desktop.Views;

public sealed partial class MainWindow : Window
{
    private readonly ConnectionProfileManager _profileManager;
    private readonly FirebirdSessionManager _sessionManager;
    private readonly ISecureCredentialStore _credentialStore;
    private readonly StudioPreferencesStore _preferencesStore = new();
    private readonly GitHubReleaseChecker _releaseChecker = new();
    private readonly GitHubUpdateDownloader _updateDownloader = new();
    private readonly SemaphoreSlim _updateCheckGate = new(1, 1);
    private readonly CancellationTokenSource _updateCheckLifetime = new();
    private Task _updateCheckTask = Task.CompletedTask;
    private string? _lastNotifiedReleaseTag;
    private bool _isPreparedForUpdateShutdown;
    private readonly QueryHistoryStore _queryHistoryStore = new();
    private readonly SqlEditorRecoveryStore _sqlEditorRecoveryStore = new();
    private StudioPreferences _preferences = StudioPreferences.Default;
    private readonly ObservableCollection<ConnectionProfile> _profiles = [];
    private readonly BatchObservableCollection<DatabaseMetadataObject> _metadataObjects = [];
    private CancellationTokenSource? _metadataLoadingCancellation;
    private readonly Dictionary<string, CancellationTokenSource> _metadataPageCancellations = new(StringComparer.OrdinalIgnoreCase);
    private int _metadataLoadGeneration;
    private readonly ObservableCollection<QueryHistoryEntry> _queryHistory = [];
    private CancellationTokenSource? _queryExecutionCancellation;
    private CancellationTokenSource? _recoverySaveCancellation;
    private Task _recoverySaveTask = Task.CompletedTask;
    private CancellationTokenSource? _footerNotificationCancellation;
    private readonly ObservableCollection<StudioMessageEntry> _messages = [];
    private readonly MenuItem _connectionContextEditItem;
    private readonly MenuItem _connectionContextConnectItem;
    private readonly MenuItem _connectionContextDisconnectItem;
    private readonly MenuItem _connectionContextDeleteItem;
    private bool _isRestoringEditors;
    private bool _isQueryExecuting;
    private readonly HashSet<string> _loadingMetadataCategories = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _metadataNextOffsets = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _metadataCategoriesWithMore = new(StringComparer.OrdinalIgnoreCase);
    private ScrollViewer? _metadataTreeScrollViewer;
    private bool _isMetadataSearchActive;
    private bool _isMetadataRefreshRunning;
    private CancellationTokenSource? _metadataSearchCancellation;
    private const int MetadataPageSize = 200;
    private const int TableDataPageSize = 200;
    private readonly Dictionary<Guid, IReadOnlyList<DatabaseMetadataObject>> _completionMetadataCache = [];
    private readonly Dictionary<Guid, IReadOnlyList<DatabaseMetadataObject>> _completionDomainCache = [];
    private CompletionWindow? _completionWindow;
    private int _currentTableDataOffset;
    private DatabaseTableDataPage? _currentTableDataPage;
    private readonly ListBox _connectionsList;
    private readonly TreeView _metadataTree;
    private readonly TextBox _metadataSearchBox;
    private readonly Button _refreshMetadataButton;
    private readonly MenuItem _refreshMetadataMenuItem;
    private readonly MenuItem _moveCategoryUpMenuItem;
    private readonly MenuItem _moveCategoryDownMenuItem;
    private readonly MenuItem _executeButton;
    private readonly MenuItem _sqlAutocompleteMenuItem;
    private readonly MenuItem _cancelQueryButton;
    private readonly ListBox _queryHistoryList;
    private readonly ListBox _queryHistoryTabList;
    private readonly TableView _queryResultsGrid;
    private readonly TabControl _editorTabs;
    private readonly List<(TabItem Tab, AvaloniaEdit.TextEditor Editor)> _sqlEditors = [];
    private readonly List<AvaloniaEdit.TextEditor> _readOnlySqlEditors = [];
    private readonly TabItem _addSqlEditorTab;
    private readonly TabItem _tableDetailsTab;
    private readonly TextBlock _tableDetailsTitle;
    private readonly TableView _tableFieldsGrid;
    private readonly TableView _tableDataGrid;
    private readonly Button _previousTableDataPageButton;
    private readonly Button _nextTableDataPageButton;
    private readonly TextBlock _tableDataPageStatus;
    private readonly ListBox _tableConstraintsList;
    private readonly AvaloniaEdit.TextEditor _tableDdlText;
    private readonly Grid _workspaceGrid;
    private readonly GridSplitter _historySplitter;
    private readonly Border _queryHistoryPanel;
    private readonly TabItem _queryHistoryTab;
    private readonly MenuItem _historyDockRightMenuItem;
    private readonly MenuItem _historyDockTabMenuItem;
    private readonly MenuItem _toggleThemeMenuItem;
    private readonly MenuItem _checkForUpdatesMenuItem;
    private readonly MenuItem _editButton;
    private readonly MenuItem _removeButton;
    private readonly MenuItem _connectButton;
    private readonly MenuItem _disconnectButton;
    private readonly TextBlock _statusText;
    private readonly ItemsControl _messageLogItems;
    private readonly ScrollViewer _messagesScrollViewer;
    private readonly TabControl _resultsMessagesTabs;
    private readonly TabItem _resultsTab;
    private readonly TabItem _messagesTab;
    private readonly TextBlock _footerNotificationText;
    private readonly TextBlock _connectionStateText;
    private readonly TextBlock _currentVersionText;
    private string? _currentTableDetailsName;

    public MainWindow()
    {
        AvaloniaXamlLoader.Load(this);

        var sqlEditor = this.FindControl<AvaloniaEdit.TextEditor>("SqlEditor")!;
        var sqlEditorTab = this.FindControl<TabItem>("SqlEditorTab")!;
        ConfigureSqlEditorTab(sqlEditorTab, sqlEditor);
        sqlEditor.TextArea.TextView.LineTransformers.Add(new FirebirdSqlSyntaxColorizer(() => _preferences.Theme == "Dark"));

        var repository = new JsonConnectionProfileRepository();
        _profileManager = new ConnectionProfileManager(repository);
        _sessionManager = new FirebirdSessionManager();
        _credentialStore = new LatchkeySecureCredentialStore();

        _connectionsList = this.FindControl<ListBox>("ConnectionsList")!;
        var createConnectionContextItem = new MenuItem { Header = "Create connection…" };
        _connectionContextEditItem = new MenuItem { Header = "Edit connection…", IsEnabled = false };
        _connectionContextConnectItem = new MenuItem { Header = "Connect to database", IsEnabled = false };
        _connectionContextDisconnectItem = new MenuItem { Header = "Disconnect from database", IsEnabled = false };
        _connectionContextDeleteItem = new MenuItem { Header = "Delete connection", IsEnabled = false };
        _connectionsList.ContextMenu = new ContextMenu
        {
            Items =
            {
                createConnectionContextItem,
                new Separator(),
                _connectionContextEditItem,
                _connectionContextConnectItem,
                _connectionContextDisconnectItem,
                new Separator(),
                _connectionContextDeleteItem
            }
        };
        _metadataTree = this.FindControl<TreeView>("MetadataTree")!;
        _metadataSearchBox = this.FindControl<TextBox>("MetadataSearchBox")!;
        _refreshMetadataButton = this.FindControl<Button>("RefreshMetadataButton")!;
        _refreshMetadataMenuItem = this.FindControl<MenuItem>("RefreshMetadataMenuItem")!;
        _moveCategoryUpMenuItem = this.FindControl<MenuItem>("MoveCategoryUpMenuItem")!;
        _moveCategoryDownMenuItem = this.FindControl<MenuItem>("MoveCategoryDownMenuItem")!;
        _executeButton = this.FindControl<MenuItem>("ExecuteMenuItem")!;
        _sqlAutocompleteMenuItem = this.FindControl<MenuItem>("SqlAutocompleteMenuItem")!;
        _sqlAutocompleteMenuItem.Click += async (_, _) => await ShowSqlCompletionAsync();
        this.FindControl<MenuItem>("FormatSqlMenuItem")!.Click += (_, _) => FormatActiveSql();
        _cancelQueryButton = this.FindControl<MenuItem>("CancelQueryMenuItem")!;
        _queryHistoryList = this.FindControl<ListBox>("QueryHistoryList")!;
        _queryHistoryTabList = this.FindControl<ListBox>("QueryHistoryTabList")!;
        _queryResultsGrid = this.FindControl<TableView>("QueryResultsGrid")!;
        _editorTabs = this.FindControl<TabControl>("EditorTabs")!;
        _addSqlEditorTab = this.FindControl<TabItem>("AddSqlEditorTab")!;
        _editorTabs.SelectionChanged += (_, _) =>
        {
            if (!_isRestoringEditors && !ReferenceEquals(_editorTabs.SelectedItem, _addSqlEditorTab))
                ScheduleSqlEditorRecoverySave(showNotification: false);
        };
        if (_addSqlEditorTab.Header is Grid addEditorHeader &&
            addEditorHeader.Children.OfType<Button>().FirstOrDefault() is { } addEditorButton)
            addEditorButton.Click += (_, _) => CreateSqlEditorTab();
        _tableDetailsTab = this.FindControl<TabItem>("TableDetailsTab")!;
        var closeTableDetailsItem = new MenuItem { Header = "Close tab" };
        closeTableDetailsItem.Click += (_, _) => CloseTableDetailsTab();
        _tableDetailsTab.ContextMenu = new ContextMenu { Items = { closeTableDetailsItem } };
        EnableMiddleClickClose(_tableDetailsTab, () => { CloseTableDetailsTab(); return Task.CompletedTask; });
        _tableDetailsTitle = this.FindControl<TextBlock>("TableDetailsTitle")!;
        _tableFieldsGrid = this.FindControl<TableView>("TableFieldsGrid")!;
        _tableDataGrid = this.FindControl<TableView>("TableDataGrid")!;
        _previousTableDataPageButton = this.FindControl<Button>("PreviousTableDataPageButton")!;
        _nextTableDataPageButton = this.FindControl<Button>("NextTableDataPageButton")!;
        _tableDataPageStatus = this.FindControl<TextBlock>("TableDataPageStatus")!;
        _tableConstraintsList = this.FindControl<ListBox>("TableConstraintsList")!;
        _tableConstraintsList.DoubleTapped += OnConstraintDoubleTapped;
        _tableDdlText = this.FindControl<AvaloniaEdit.TextEditor>("TableDdlText")!;
        _tableDdlText.TextArea.TextView.LineTransformers.Add(new FirebirdSqlSyntaxColorizer(() => _preferences.Theme == "Dark"));
        _readOnlySqlEditors.Add(_tableDdlText);
        _workspaceGrid = this.FindControl<Grid>("WorkspaceGrid")!;
        _historySplitter = this.FindControl<GridSplitter>("HistorySplitter")!;
        _queryHistoryPanel = this.FindControl<Border>("QueryHistoryPanel")!;
        _queryHistoryTab = this.FindControl<TabItem>("QueryHistoryTab")!;
        _historyDockRightMenuItem = this.FindControl<MenuItem>("HistoryDockRightMenuItem")!;
        _historyDockTabMenuItem = this.FindControl<MenuItem>("HistoryDockTabMenuItem")!;
        _toggleThemeMenuItem = this.FindControl<MenuItem>("ToggleThemeMenuItem")!;
        _checkForUpdatesMenuItem = this.FindControl<MenuItem>("CheckForUpdatesMenuItem")!;
        _editButton = this.FindControl<MenuItem>("EditConnectionMenuItem")!;
        _removeButton = this.FindControl<MenuItem>("RemoveConnectionMenuItem")!;
        _connectButton = this.FindControl<MenuItem>("ConnectMenuItem")!;
        _disconnectButton = this.FindControl<MenuItem>("DisconnectMenuItem")!;
        _statusText = this.FindControl<TextBlock>("StatusText")!;
        _messageLogItems = this.FindControl<ItemsControl>("MessageLogItems")!;
        _messageLogItems.ItemsSource = _messages;
        _messagesScrollViewer = this.FindControl<ScrollViewer>("MessagesScrollViewer")!;
        _resultsMessagesTabs = this.FindControl<TabControl>("ResultsMessagesTabs")!;
        _resultsTab = this.FindControl<TabItem>("ResultsTab")!;
        _messagesTab = this.FindControl<TabItem>("MessagesTab")!;
        this.FindControl<Button>("ClearMessagesButton")!.Click += (_, _) => ClearMessages();
        this.FindControl<Button>("ClearHistoryDockButton")!.Click += async (_, _) => await ClearQueryHistoryAsync();
        this.FindControl<Button>("ClearHistoryTabButton")!.Click += async (_, _) => await ClearQueryHistoryAsync();
        this.FindControl<Button>("ClearResultsButton")!.Click += (_, _) => ClearResults();
        var toggleConnectionDisplayButton = this.FindControl<Button>("ToggleConnectionDisplayButton")!;
        toggleConnectionDisplayButton.Click += async (_, _) =>
            await SetConnectionDisplayModeAsync(_preferences.ConnectionDisplayMode == "Detailed" ? "Minimal" : "Detailed");
        _footerNotificationText = this.FindControl<TextBlock>("FooterNotificationText")!;
        _connectionStateText = this.FindControl<TextBlock>("ConnectionStateText")!;
        _currentVersionText = this.FindControl<TextBlock>("CurrentVersionText")!;
        var currentVersion = GitHubReleaseChecker.NormalizeVersion(
            typeof(MainWindow).Assembly.GetName().Version ?? new Version(0, 1, 0));
        Title = $"Firebird Studio - {currentVersion}";
        _currentVersionText.Text = $"Firebird Studio {currentVersion}";

        _connectionsList.ItemsSource = _profiles;
        _connectionsList.ItemTemplate = CreateConnectionTemplate(showDetails: true);
        var detailedConnectionsMenuItem = this.FindControl<MenuItem>("ConnectionDisplayDetailedMenuItem")!;
        var minimalConnectionsMenuItem = this.FindControl<MenuItem>("ConnectionDisplayMinimalMenuItem")!;
        detailedConnectionsMenuItem.Click += async (_, _) => await SetConnectionDisplayModeAsync("Detailed");
        minimalConnectionsMenuItem.Click += async (_, _) => await SetConnectionDisplayModeAsync("Minimal");
        _metadataTree.ItemsSource = _metadataObjects;
        _queryHistoryList.ItemsSource = _queryHistory;
        _queryHistoryTabList.ItemsSource = _queryHistory;
        _refreshMetadataButton.Click += OnRefreshMetadataClick;
        _previousTableDataPageButton.Click += async (_, _) => await ChangeTableDataPageAsync(-1);
        _nextTableDataPageButton.Click += async (_, _) => await ChangeTableDataPageAsync(1);
        _moveCategoryUpMenuItem.Click += OnMoveCategoryUpClick;
        _moveCategoryDownMenuItem.Click += OnMoveCategoryDownClick;
        _executeButton.Click += OnExecuteClick;
        _cancelQueryButton.Click += OnCancelQueryClick;
        _queryHistoryList.DoubleTapped += OnQueryHistoryDoubleTapped;
        _queryHistoryTabList.DoubleTapped += OnQueryHistoryDoubleTapped;
        _connectionsList.SelectionChanged += OnSelectionChanged;
        _connectionsList.DoubleTapped += OnConnectionsDoubleTapped;
        _connectionsList.PointerPressed += OnConnectionsPointerPressed;
        createConnectionContextItem.Click += OnAddConnectionClick;
        _connectionContextEditItem.Click += OnEditConnectionClick;
        _connectionContextConnectItem.Click += OnConnectClick;
        _connectionContextDisconnectItem.Click += OnDisconnectClick;
        _connectionContextDeleteItem.Click += OnRemoveConnectionClick;
        _metadataSearchBox.TextChanged += OnMetadataSearchChanged;
        this.FindControl<ComboBox>("MetadataSearchKindFilter")!.SelectionChanged += OnMetadataSearchChanged;
        _metadataTree.SelectionChanged += OnMetadataSelectionChanged;
        _metadataTree.DoubleTapped += OnMetadataDoubleTapped;
        _metadataTree.AttachedToVisualTree += (_, _) =>
            Dispatcher.UIThread.Post(AttachMetadataTreeScrollViewer);
        this.FindControl<MenuItem>("AddConnectionMenuItem")!.Click += OnAddConnectionClick;
        this.FindControl<MenuItem>("ExitMenuItem")!.Click += (_, _) => Close();
        _editButton.Click += OnEditConnectionClick;
        _removeButton.Click += OnRemoveConnectionClick;
        _connectButton.Click += OnConnectClick;
        _disconnectButton.Click += OnDisconnectClick;
        _refreshMetadataMenuItem.Click += OnRefreshMetadataClick;
        _toggleThemeMenuItem.Click += async (_, _) =>
            await SetThemeAsync(_preferences.Theme == "Dark" ? "Light" : "Dark");
        _checkForUpdatesMenuItem.Click += async (_, _) =>
            await CheckForUpdatesAsync(manual: true, cancellationToken: _updateCheckLifetime.Token);
        _historyDockRightMenuItem.Click += (_, _) => SetHistoryDockMode(true);
        _historyDockTabMenuItem.Click += (_, _) => SetHistoryDockMode(false);
        KeyDown += OnWindowKeyDown;
        Opened += OnWindowOpened;
        Closed += OnWindowClosed;
    }

    private async void OnWindowOpened(object? sender, EventArgs e)
    {
        try
        {
            _preferences = await _preferencesStore.LoadAsync();
            if (_preferences.Theme == "Default")
                _preferences = _preferences with { Theme = "Light" };
            ApplyTheme();
            ApplyConnectionDisplayMode();
            SetHistoryDockMode(true);
        }
        catch (Exception exception)
        {
            ReportError("Loading application settings; defaults are being used", exception);
        }

        try
        {
            var history = await _queryHistoryStore.LoadAsync();
            _queryHistory.Clear();
            foreach (var entry in history)
                _queryHistory.Add(entry);
        }
        catch (Exception exception)
        {
            ReportError("Loading query history", exception);
        }

        try
        {
            var profiles = await _profileManager.GetAllAsync();
            _profiles.Clear();
            foreach (var profile in profiles.OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
                _profiles.Add(profile);

            _statusText.Text = $"Loaded {_profiles.Count} saved connection profile(s). Passwords are not stored in the profile file.";
            UpdateConnectionState();
        }
        catch (Exception exception)
        {
            ReportError("Loading connection profiles", exception);
        }

        try
        {
            await RestoreSqlEditorRecoveryAsync();
        }
        catch (Exception exception)
        {
            ReportError("SQL editor recovery", exception);
        }

        // This loop checks once immediately, then every 24 hours without blocking startup.
        _updateCheckTask = RunUpdateCheckLoopAsync(_updateCheckLifetime.Token);
    }

    private async Task RunUpdateCheckLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await CheckForUpdatesAsync(manual: false, cancellationToken: cancellationToken);
            try
            {
                await Task.Delay(TimeSpan.FromHours(24), cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
        }
    }

    private async Task CheckForUpdatesAsync(bool manual, CancellationToken cancellationToken)
    {
        bool acquired;
        try
        {
            acquired = await _updateCheckGate.WaitAsync(0, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        if (!acquired)
        {
            if (manual)
                ShowFooterNotification("An update check is already running", "#9A7100");
            return;
        }

        try
        {
            if (manual)
                ShowFooterNotification("Checking for updates…", "#9A7100");

            var latestRelease = await _releaseChecker.GetLatestStableReleaseAsync(cancellationToken);
            if (latestRelease is null)
            {
                if (manual)
                    ShowFooterNotification("No stable release has been published yet", "#9A7100");
                return;
            }

            var assemblyVersion = typeof(MainWindow).Assembly.GetName().Version ?? new Version(0, 0, 0);
            var currentVersion = GitHubReleaseChecker.NormalizeVersion(assemblyVersion);
            if (latestRelease.Version <= currentVersion)
            {
                if (manual)
                    ShowFooterNotification("Firebird Studio is up to date", "#397B45");
                return;
            }

            if (string.Equals(_lastNotifiedReleaseTag, latestRelease.TagName, StringComparison.OrdinalIgnoreCase))
                return;

            _lastNotifiedReleaseTag = latestRelease.TagName;
            await ShowUpdateAvailableDialogAsync(latestRelease, currentVersion);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            if (manual)
                ReportError("Checking for updates", exception);
            else
                AddMessage("Warning", "Automatic update check", exception.Message);
        }
        finally
        {
            _updateCheckGate.Release();
        }
    }

    private async Task ShowUpdateAvailableDialogAsync(AvailableRelease release, Version currentVersion)
    {
        var notes = string.IsNullOrWhiteSpace(release.ReleaseNotes)
            ? "No release notes were provided."
            : release.ReleaseNotes.Trim();
        if (notes.Length > 8_000)
            notes = notes[..8_000] + Environment.NewLine + "[Release notes truncated.]";

        var dialog = new Window
        {
            Title = "Firebird Studio update available",
            Width = 560,
            SizeToContent = SizeToContent.Height,
            MaxHeight = 620,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false
        };

        var viewReleaseButton = CreateDialogButton("View release", () =>
        {
            dialog.Close();
            try
            {
                Process.Start(new ProcessStartInfo(release.HtmlUrl.AbsoluteUri) { UseShellExecute = true });
            }
            catch (Exception exception)
            {
                ReportError("Opening the GitHub release page", exception);
            }
        });
        var laterButton = CreateDialogButton("Later", () => dialog.Close());
        var downloadButton = CreateDialogButton("Download update", () =>
        {
            dialog.Close();
            _ = DownloadUpdateAsync(release);
        });

        dialog.Content = new StackPanel
        {
            Margin = new Thickness(18),
            Spacing = 12,
            Children =
            {
                new TextBlock
                {
                    Text = $"Version {release.TagName} is available. You are using version {currentVersion}.",
                    FontSize = 14,
                    FontWeight = FontWeight.SemiBold,
                    TextWrapping = TextWrapping.Wrap
                },
                new TextBlock { Text = "Release notes", FontWeight = FontWeight.SemiBold },
                new ScrollViewer
                {
                    MaxHeight = 300,
                    Content = new TextBlock { Text = notes, TextWrapping = TextWrapping.Wrap }
                },
                new StackPanel
                {
                    Orientation = Avalonia.Layout.Orientation.Horizontal,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                    Spacing = 8,
                    Children = { laterButton, viewReleaseButton, downloadButton }
                }
            }
        };

        await dialog.ShowDialog(this);
    }

    private async Task DownloadUpdateAsync(AvailableRelease release)
    {
        using var cancellation = new CancellationTokenSource();
        DownloadedUpdate? downloadedUpdate = null;
        var installRequested = false;
        var progressBar = new ProgressBar
        {
            Minimum = 0,
            Maximum = 100,
            Value = 0,
            Height = 12
        };
        var statusText = new TextBlock
        {
            Text = "Preparing the release download…",
            TextWrapping = TextWrapping.Wrap
        };
        string? downloadDirectory = null;
        var openFolderButton = CreateDialogButton("Open download folder", () =>
        {
            if (downloadDirectory is not { } directory || !Directory.Exists(directory))
                return;

            try
            {
                Process.Start(new ProcessStartInfo(directory) { UseShellExecute = true });
            }
            catch (Exception exception)
            {
                ReportError("Opening the update download folder", exception);
            }
        });
        openFolderButton.IsVisible = false;

        var dialog = new Window
        {
            Title = $"Download Firebird Studio {release.Version}",
            Width = 520,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false
        };

        var installButton = CreateDialogButton("Install and restart", () =>
        {
            installRequested = true;
            dialog.Close();
        });
        installButton.IsVisible = false;

        var cancelButton = new Button { Content = "Cancel download" };
        cancelButton.Click += (_, _) =>
        {
            if (cancelButton.Content?.ToString() == "Close")
                dialog.Close();
            else
                cancellation.Cancel();
        };

        dialog.Content = new StackPanel
        {
            Margin = new Thickness(18),
            Spacing = 12,
            Children =
            {
                new TextBlock
                {
                    Text = $"Downloading Firebird Studio {release.Version}",
                    FontSize = 14,
                    FontWeight = FontWeight.SemiBold
                },
                statusText,
                progressBar,
                new StackPanel
                {
                    Orientation = Avalonia.Layout.Orientation.Horizontal,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                    Spacing = 8,
                    Children = { installButton, openFolderButton, cancelButton }
                }
            }
        };

        dialog.Closed += (_, _) => cancellation.Cancel();
        dialog.Opened += async (_, _) =>
        {
            try
            {
                var progress = new Progress<UpdateDownloadProgress>(value =>
                    Dispatcher.UIThread.Post(() =>
                    {
                        if (!dialog.IsVisible)
                            return;
                        progressBar.Value = value.Percentage;
                        statusText.Text = value.TotalBytes > 0
                            ? $"Downloading… {value.Percentage:F0}% ({FormatBytes(value.BytesReceived)} of {FormatBytes(value.TotalBytes)})"
                            : $"Downloading… {FormatBytes(value.BytesReceived)}";
                    }));

                downloadedUpdate = await _updateDownloader.DownloadAsync(
                    release, progress, cancellation.Token);

                progressBar.Value = 100;
                downloadDirectory = downloadedUpdate.DirectoryPath;
                openFolderButton.IsVisible = true;
                cancelButton.Content = "Close";

                var updaterExecutable = GetUpdaterExecutablePath();
                if (File.Exists(updaterExecutable))
                {
                    statusText.Text =
                        $"Download complete and SHA-256 verified.\n\n{downloadedUpdate.FileName}\n\nInstall and restart Firebird Studio to apply this update.";
                    installButton.IsVisible = true;
                }
                else
                {
                    var reason = "Automatic installation is available in published packages. Development builds can still use manual extraction.";
                    statusText.Text =
                        $"Download complete. SHA-256 verified. The ZIP package is ready for manual extraction. {reason}\n\n{downloadedUpdate.FileName}";
                }
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
                if (dialog.IsVisible)
                {
                    statusText.Text = "Download canceled. Incomplete files were removed.";
                    cancelButton.Content = "Close";
                }
            }
            catch (Exception exception)
            {
                if (dialog.IsVisible)
                {
                    statusText.Text = $"Download failed: {exception.Message}";
                    ReportError("Downloading or verifying the update package", exception);
                    cancelButton.Content = "Close";
                }
            }
        };

        await dialog.ShowDialog(this);

        if (installRequested && downloadedUpdate is not null)
        {
            try
            {
                StartExternalUpdater(downloadedUpdate);
                await PrepareForUpdateShutdownAsync();
                Close();
            }
            catch (Exception exception)
            {
                ReportError("Starting the external update installer", exception);
            }
        }
    }

    private static string GetUpdaterExecutablePath() =>
        Path.Combine(
            AppContext.BaseDirectory,
            "Updater",
            OperatingSystem.IsWindows() ? "FirebirdStudio.Updater.exe" : "FirebirdStudio.Updater");

    private void StartExternalUpdater(DownloadedUpdate update)
    {
        if (_isQueryExecuting)
            throw new InvalidOperationException("Wait for the running SQL query to finish before installing an update.");

        var installDirectory = Path.GetFullPath(AppContext.BaseDirectory);
        var executablePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executablePath))
            throw new InvalidOperationException("The current application executable path could not be determined.");

        executablePath = Path.GetFullPath(executablePath);
        var relativeExecutablePath = Path.GetRelativePath(installDirectory, executablePath);
        if (Path.IsPathRooted(relativeExecutablePath) ||
            relativeExecutablePath.Equals("..", StringComparison.Ordinal) ||
            relativeExecutablePath.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new InvalidOperationException(
                "Automatic installation is available for packaged installations, not for applications started through the .NET development host.");

        var bundledUpdaterDirectory = Path.Combine(installDirectory, "Updater");
        var bundledUpdaterExecutable = GetUpdaterExecutablePath();
        if (!File.Exists(bundledUpdaterExecutable))
            throw new FileNotFoundException(
                "The external updater application is missing from this installation.",
                bundledUpdaterExecutable);

        // Run from the writable cache, not from the directory that the updater must replace.
        var updaterRuntimeDirectory = Path.Combine(update.DirectoryPath, "UpdaterRuntime");
        if (Directory.Exists(updaterRuntimeDirectory))
            Directory.Delete(updaterRuntimeDirectory, recursive: true);
        CopyDirectory(bundledUpdaterDirectory, updaterRuntimeDirectory);

        var updaterExecutable = Path.Combine(
            updaterRuntimeDirectory,
            OperatingSystem.IsWindows() ? "FirebirdStudio.Updater.exe" : "FirebirdStudio.Updater");
        if (!File.Exists(updaterExecutable))
            throw new FileNotFoundException("The copied updater executable is missing.", updaterExecutable);

        var startInfo = new ProcessStartInfo(updaterExecutable)
        {
            UseShellExecute = false,
            CreateNoWindow = false,
            WorkingDirectory = installDirectory
        };
        AddArgument(startInfo, "--parent-pid", Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
        AddArgument(startInfo, "--archive", update.FilePath);
        AddArgument(startInfo, "--expected-size", update.SizeBytes.ToString(CultureInfo.InvariantCulture));
        AddArgument(startInfo, "--expected-sha256", update.Sha256);
        AddArgument(startInfo, "--install-dir", installDirectory);
        AddArgument(startInfo, "--app-path", executablePath);
        AddArgument(startInfo, "--log-path", Path.Combine(update.DirectoryPath, "updater.log"));

        _ = Process.Start(startInfo)
            ?? throw new InvalidOperationException("The operating system did not start the external updater.");
    }

    private static void AddArgument(ProcessStartInfo startInfo, string name, string value)
    {
        startInfo.ArgumentList.Add(name);
        startInfo.ArgumentList.Add(value);
    }

    private static void CopyDirectory(string sourceDirectory, string destinationDirectory)
    {
        Directory.CreateDirectory(destinationDirectory);
        foreach (var directory in Directory.EnumerateDirectories(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceDirectory, directory);
            Directory.CreateDirectory(Path.Combine(destinationDirectory, relativePath));
        }

        foreach (var file in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceDirectory, file);
            var destinationPath = Path.Combine(destinationDirectory, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            File.Copy(file, destinationPath, overwrite: true);
            if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
                File.SetUnixFileMode(destinationPath, File.GetUnixFileMode(file));
        }
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        double size = bytes;
        var unit = 0;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }

        return $"{size:0.##} {units[unit]}";
    }

    private async void OnAddConnectionClick(object? sender, RoutedEventArgs e) =>
        await ShowConnectionEditorAsync(new ConnectionProfile());

    private async void OnEditConnectionClick(object? sender, RoutedEventArgs e)
    {
        if (_connectionsList.SelectedItem is ConnectionProfile profile)
            await ShowConnectionEditorAsync(profile);
    }

    private async Task ShowConnectionEditorAsync(ConnectionProfile profile)
    {
        var editor = new ConnectionEditorWindow(profile, _profileManager, new FirebirdConnectionTester());
        await editor.ShowDialog(this);

        if (editor.SavedProfile is not { } savedProfile)
            return;

        var existingIndex = _profiles.ToList().FindIndex(item => item.Id == savedProfile.Id);
        if (existingIndex >= 0)
            _profiles[existingIndex] = savedProfile;
        else
            _profiles.Add(savedProfile);

        _statusText.Text = $"Connection profile '{savedProfile.Name}' saved. No password was written to disk.";
        _connectionsList.SelectedItem = savedProfile;
    }

    private async void OnRemoveConnectionClick(object? sender, RoutedEventArgs e)
    {
        if (_connectionsList.SelectedItem is not ConnectionProfile profile)
            return;

        if (_sessionManager.GetActiveSessions().Any(session => session.ProfileId == profile.Id))
        {
            _statusText.Text = "Disconnect this profile before removing it.";
            return;
        }

        if (!await ConfirmDeleteConnectionAsync(profile))
            return;

        try
        {
            // Remove the profile first. If persistence fails, keep its saved credential intact.
            await _profileManager.DeleteAsync(profile.Id);
            _profiles.Remove(profile);

            try
            {
                await _credentialStore.DeletePasswordAsync(profile.Id);
                _statusText.Text = $"Connection profile '{profile.Name}' removed. The database itself was not changed.";
            }
            catch (Exception credentialException)
            {
                ReportError($"Removing the saved credential for connection profile '{profile.Name}'", credentialException);
            }
        }
        catch (Exception exception)
        {
            ReportError($"Removing connection profile '{profile.Name}'", exception);
        }
    }

    private async Task<bool> ConfirmDeleteConnectionAsync(ConnectionProfile profile)
    {
        var confirmation = new Window
        {
            Title = "Delete connection",
            Width = 420,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false
        };

        void CloseConfirmation(bool result) => confirmation.Close(result);

        confirmation.Content = new StackPanel
        {
            Margin = new Thickness(18),
            Spacing = 14,
            Children =
            {
                new TextBlock
                {
                    Text = $"Delete the saved connection '{profile.Name}'? The database and its files will not be changed.",
                    TextWrapping = TextWrapping.Wrap
                },
                new StackPanel
                {
                    Orientation = Avalonia.Layout.Orientation.Horizontal,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                    Spacing = 8,
                    Children =
                    {
                        CreateDialogButton("Cancel", () => CloseConfirmation(false)),
                        CreateDialogButton("Delete", () => CloseConfirmation(true))
                    }
                }
            }
        };

        return await confirmation.ShowDialog<bool>(this);
    }

    private static Button CreateDialogButton(string text, Action onClick)
    {
        var button = new Button { Content = text, MinWidth = 84 };
        button.Click += (_, _) => onClick();
        return button;
    }

    private async void OnConnectClick(object? sender, RoutedEventArgs e)
    {
        if (_connectionsList.SelectedItem is ConnectionProfile profile)
            await ConnectProfileAsync(profile);
    }

    private void OnConnectionsPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(_connectionsList).Properties.IsRightButtonPressed)
            return;

        if (e.Source is Visual source)
        {
            var item = source as ListBoxItem ?? source.GetVisualAncestors().OfType<ListBoxItem>().FirstOrDefault();
            if (item?.DataContext is ConnectionProfile profile)
                _connectionsList.SelectedItem = profile;
        }

        UpdateActionAvailability();
        UpdateConnectionContextMenuAvailability();
    }

    private void UpdateConnectionContextMenuAvailability()
    {
        var selected = _connectionsList.SelectedItem as ConnectionProfile;
        var isConnected = selected is not null &&
            _sessionManager.GetActiveSessions().Any(session => session.ProfileId == selected.Id);
        _connectionContextEditItem.IsEnabled = selected is not null;
        _connectionContextConnectItem.IsEnabled = selected is not null && !isConnected;
        _connectionContextDisconnectItem.IsEnabled = selected is not null && isConnected;
        _connectionContextDeleteItem.IsEnabled = selected is not null && !isConnected;
    }

    private async void OnConnectionsDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (_connectionsList.SelectedItem is ConnectionProfile profile)
            await ConnectProfileAsync(profile);
    }

    private async Task ConnectProfileAsync(ConnectionProfile profile)
    {
        if (_sessionManager.GetActiveSessions().Any(session => session.ProfileId == profile.Id))
        {
            _statusText.Text = $"'{profile.Name}' is already connected.";
            return;
        }

        string? savedPassword = null;
        string? credentialStoreNotice = null;
        try
        {
            savedPassword = await _credentialStore.GetPasswordAsync(profile.Id);
        }
        catch (Exception exception)
        {
            credentialStoreNotice = $"The operating system credential store is unavailable. You can still connect by entering the password manually. Details: {exception.Message}";
            ReportError("Accessing the operating system credential store", exception);
        }

        var passwordPrompt = new ConnectionPasswordWindow(profile.Name, savedPassword, credentialStoreNotice);
        await passwordPrompt.ShowDialog(this);
        if (passwordPrompt.Password is not { } password)
        {
            savedPassword = null;
            return;
        }

        _connectButton.IsEnabled = false;
        _statusText.Text = $"Connecting to '{profile.Name}'...";
        try
        {
            var result = await _sessionManager.ConnectAsync(profile, password);
            if (result.Succeeded)
            {
                _statusText.Text = result.Message;
                AddMessage("Success", $"Connection to '{profile.Name}'", result.Message);
                ShowFooterNotification("Database connection established", "#397B45");
            }
            else
                ReportError($"Connection to '{profile.Name}'", result.Diagnostics ?? result.Message);
            UpdateConnectionState();

            if (result.Succeeded)
            {
                _statusText.Text = "Connected. Loading database objects in the background; the window remains responsive.";
                AddMessage("Information", "Database metadata", "Loading the first 200 objects from each category in the background. The window remains responsive; database requests may wait briefly while metadata is read.");
                await RefreshMetadataAsync(profile);
                try
                {
                    if (passwordPrompt.RememberPassword && password.Length > 0)
                    {
                        await _credentialStore.SavePasswordAsync(profile.Id, password);
                        _statusText.Text = "Connected. Password saved in the operating system credential store.";
                    }
                    else if (passwordPrompt.RememberPassword)
                    {
                        await _credentialStore.DeletePasswordAsync(profile.Id);
                        _statusText.Text = "Connected. An empty password was not saved; you will be prompted next time.";
                    }
                    else if (savedPassword is not null)
                    {
                        await _credentialStore.DeletePasswordAsync(profile.Id);
                        _statusText.Text = "Connected. Previously saved password removed from the operating system credential store.";
                    }
                }
                catch (Exception exception)
                {
                    ReportError("Saving or removing the connection credential after connecting", exception);
                }
            }
        }
        catch (OperationCanceledException)
        {
            _statusText.Text = "Connection attempt cancelled.";
        }
        catch (Exception exception)
        {
            ReportError($"Connection to '{profile.Name}'", exception, password);
        }
        finally
        {
            password = string.Empty;
            savedPassword = null;
            credentialStoreNotice = null;
            passwordPrompt.ClearCapturedPassword();
            UpdateActionAvailability();
        }
    }

    private async void OnDisconnectClick(object? sender, RoutedEventArgs e)
    {
        CancelMetadataLoading();
        if (_connectionsList.SelectedItem is not ConnectionProfile profile)
            return;

        _disconnectButton.IsEnabled = false;
        try
        {
            await _sessionManager.DisconnectAsync(profile.Id);
            _metadataObjects.Clear();
            _currentTableDetailsName = null;
            _tableDetailsTab.IsVisible = false;
            _statusText.Text = $"Disconnected from '{profile.Name}'.";
            ReportInformation("Database connection", _statusText.Text);
        }
        catch (Exception exception)
        {
            ReportError($"Disconnecting from '{profile.Name}'", exception);
        }
        finally
        {
            UpdateConnectionState();
            UpdateActionAvailability();
        }
    }

    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        CancelMetadataLoading();
        _metadataSearchCancellation?.Cancel();
        _metadataSearchBox.Text = string.Empty;
        _isMetadataSearchActive = false;
        _metadataObjects.Clear();
        _completionWindow?.Close();
        _currentTableDetailsName = null;
        _currentTableDataPage = null;
        _tableDetailsTab.IsVisible = false;
        _previousTableDataPageButton.IsEnabled = false;
        _nextTableDataPageButton.IsEnabled = false;
        _tableDataPageStatus.Text = "No rows loaded";
        UpdateActionAvailability();
        UpdateConnectionContextMenuAvailability();
    }

    private void UpdateActionAvailability()
    {
        var selected = _connectionsList.SelectedItem as ConnectionProfile;
        var isConnected = selected is not null &&
            _sessionManager.GetActiveSessions().Any(session => session.ProfileId == selected.Id);

        _editButton.IsEnabled = selected is not null;
        _removeButton.IsEnabled = selected is not null && !isConnected;
        _connectButton.IsEnabled = selected is not null && !isConnected;
        _disconnectButton.IsEnabled = selected is not null && isConnected;
        _refreshMetadataButton.IsEnabled = selected is not null && isConnected;
        _refreshMetadataMenuItem.IsEnabled = selected is not null && isConnected;
        _executeButton.IsEnabled = selected is not null && isConnected && !_isQueryExecuting;
        _sqlAutocompleteMenuItem.IsEnabled = selected is not null && isConnected;
        _cancelQueryButton.IsEnabled = _isQueryExecuting;
        UpdateMetadataReorderAvailability();
        UpdateConnectionContextMenuAvailability();
    }

    private async void OnRefreshMetadataClick(object? sender, RoutedEventArgs e)
    {
        if (_connectionsList.SelectedItem is ConnectionProfile profile)
            await RefreshMetadataAsync(profile);
    }

    private async Task RefreshMetadataAsync(ConnectionProfile profile)
    {
        CancelMetadataLoading();
        var metadataCancellation = new CancellationTokenSource();
        _metadataLoadingCancellation = metadataCancellation;
        var loadGeneration = _metadataLoadGeneration;
        _refreshMetadataButton.IsEnabled = false;
        _isMetadataRefreshRunning = true;
        _statusText.Text = $"Preparing database object categories for '{profile.Name}'...";
        try
        {
            // Keep provider work off the UI thread as some provider operations may perform
            // synchronous work before their asynchronous I/O completes.
            var categories = await Task.Run(
                () => _sessionManager.GetMetadataAsync(profile.Id, metadataCancellation.Token),
                metadataCancellation.Token);
            if (metadataCancellation.IsCancellationRequested || loadGeneration != _metadataLoadGeneration ||
                _connectionsList.SelectedItem is not ConnectionProfile currentProfile || currentProfile.Id != profile.Id)
                return;

            _metadataObjects.Clear();
            _metadataNextOffsets.Clear();
            _metadataCategoriesWithMore.Clear();
            _loadingMetadataCategories.Clear();
            var orderedCategories = categories.OrderBy(item => GetCategoryOrderIndex(item.Name))
                .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase).ToArray();
            foreach (var category in orderedCategories)
            {
                // Create the observable child collection up front. Paging then appends in place,
                // keeping the category node and its current selection stable.
                _metadataObjects.Add(category with
                {
                    Details = "Objects load in batches of 200",
                    Children = new BatchObservableCollection<DatabaseMetadataObject>()
                });
            }

            _completionMetadataCache.Remove(profile.Id);
            _completionDomainCache.Remove(profile.Id);
            UpdateMetadataReorderAvailability();

            var failedCategories = new List<string>();
            for (var index = 0; index < orderedCategories.Length; index++)
            {
                if (_connectionsList.SelectedItem is not ConnectionProfile selectedProfile || selectedProfile.Id != profile.Id)
                    return;
                var categoryName = orderedCategories[index].Name;
                _statusText.Text = $"Loading database objects in the background: {categoryName} ({index + 1}/{orderedCategories.Length}). Large catalogs may take some time; the window remains responsive.";
                await LoadMetadataPageAsync(categoryName, 0, updateStatus: false,
                    cancellationToken: metadataCancellation.Token, loadGeneration: loadGeneration);
                if (!_metadataNextOffsets.ContainsKey(categoryName))
                    failedCategories.Add(categoryName);
            }

            _statusText.Text = failedCategories.Count == 0
                ? $"Loaded up to the first {MetadataPageSize} objects per category for '{profile.Name}'. Scroll within an expanded category to load additional batches automatically."
                : $"Metadata loading completed with errors in: {string.Join(", ", failedCategories)}. You can retry with Refresh.";
            AddMessage(failedCategories.Count == 0 ? "Success" : "Warning", "Database metadata",
                failedCategories.Count == 0
                    ? $"Initial metadata loading completed for {orderedCategories.Length} categories. Additional objects load automatically in batches of {MetadataPageSize}."
                    : $"Initial metadata loading failed for: {string.Join(", ", failedCategories)}.");
            Dispatcher.UIThread.Post(TryLoadMoreMetadataWhenVisible);
        }
        catch (OperationCanceledException) when (
            metadataCancellation.IsCancellationRequested || loadGeneration != _metadataLoadGeneration)
        {
            // A connection change or a newer metadata refresh superseded this metadata load.
        }
        catch (Exception exception)
        {
            if (loadGeneration == _metadataLoadGeneration)
                ReportError($"Preparing database metadata for '{profile.Name}'", exception);
        }
        finally
        {
            if (ReferenceEquals(_metadataLoadingCancellation, metadataCancellation))
                _metadataLoadingCancellation = null;
            metadataCancellation.Dispose();
            if (loadGeneration == _metadataLoadGeneration)
            {
                _isMetadataRefreshRunning = false;
                UpdateActionAvailability();
                Dispatcher.UIThread.Post(TryLoadMoreMetadataWhenVisible);
            }
        }
    }

    private void CancelMetadataLoading()
    {
        _metadataLoadGeneration++;
        var cancellation = _metadataLoadingCancellation;
        _metadataLoadingCancellation = null;
        if (cancellation is not null)
        {
            cancellation.Cancel();
            cancellation.Dispose();
        }

        foreach (var pageCancellation in _metadataPageCancellations.Values)
        {
            pageCancellation.Cancel();
            pageCancellation.Dispose();
        }
        _metadataPageCancellations.Clear();

        _loadingMetadataCategories.Clear();
        _metadataCategoriesWithMore.Clear();
        _isMetadataRefreshRunning = false;
    }

    private async void OnMetadataSearchChanged(object? sender, EventArgs e)
    {
        _metadataSearchCancellation?.Cancel();
        _metadataSearchCancellation = null;

        var searchTerm = _metadataSearchBox.Text?.Trim() ?? string.Empty;
        var kindFilter = (this.FindControl<ComboBox>("MetadataSearchKindFilter")?.SelectedIndex ?? 0) switch
        {
            1 => "Tables",
            2 => "Views",
            3 => "Column",
            4 => "Procedures",
            5 => "Triggers",
            6 => "Indexes",
            7 => "Domains",
            _ => null
        };
        if (searchTerm.Length == 0)
        {
            if (_isMetadataSearchActive && _connectionsList.SelectedItem is ConnectionProfile selectedProfile &&
                _sessionManager.GetActiveSessions().Any(session => session.ProfileId == selectedProfile.Id))
            {
                _isMetadataSearchActive = false;
                await RefreshMetadataAsync(selectedProfile);
            }
            return;
        }

        if (searchTerm.Length < 2)
        {
            if (_isMetadataSearchActive && _connectionsList.SelectedItem is ConnectionProfile shortSearchProfile &&
                _sessionManager.GetActiveSessions().Any(session => session.ProfileId == shortSearchProfile.Id))
            {
                _isMetadataSearchActive = false;
                await RefreshMetadataAsync(shortSearchProfile);
            }
            _statusText.Text = "Enter at least 2 characters to search database objects and columns.";
            return;
        }

        if (_connectionsList.SelectedItem is not ConnectionProfile profile ||
            !_sessionManager.GetActiveSessions().Any(session => session.ProfileId == profile.Id))
        {
            _statusText.Text = "Connect to a database before searching its objects.";
            return;
        }

        var cancellation = new CancellationTokenSource();
        _metadataSearchCancellation = cancellation;
        try
        {
            await Task.Delay(300, cancellation.Token);
            _statusText.Text = $"Searching database objects for '{searchTerm}'...";
            var results = await Task.Run(
                () => _sessionManager.SearchMetadataAsync(profile.Id, searchTerm, cancellation.Token, kindFilter),
                cancellation.Token);
            if (cancellation.IsCancellationRequested ||
                _connectionsList.SelectedItem is not ConnectionProfile currentProfile || currentProfile.Id != profile.Id ||
                !string.Equals(_metadataSearchBox.Text?.Trim(), searchTerm, StringComparison.Ordinal) ||
                !string.Equals(GetSelectedMetadataKindFilter(), kindFilter, StringComparison.Ordinal))
                return;

            _isMetadataSearchActive = true;
            _metadataObjects.Clear();
            const int searchUiBatchSize = 50;
            for (var index = 0; index < results.Count; index += searchUiBatchSize)
            {
                cancellation.Token.ThrowIfCancellationRequested();
                if (_connectionsList.SelectedItem is not ConnectionProfile activeProfile ||
                    activeProfile.Id != profile.Id)
                    return;

                _metadataObjects.AddRange(results.Skip(index).Take(searchUiBatchSize).ToArray());
                await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            }

            var filterDescription = kindFilter is null ? "database objects and columns" : kindFilter == "Column" ? "columns" : kindFilter.ToLowerInvariant();
            _statusText.Text = results.Count == 0
                ? $"No {filterDescription} match '{searchTerm}'."
                : $"Found {results.Count:N0} {filterDescription} matching '{searchTerm}'.";
        }
        catch (OperationCanceledException)
        {
            // A newer search term superseded this request.
        }
        catch (Exception exception)
        {
            if (!cancellation.IsCancellationRequested)
                ReportError($"Searching database objects for '{searchTerm}'", exception);
        }
        finally
        {
            if (ReferenceEquals(_metadataSearchCancellation, cancellation))
                _metadataSearchCancellation = null;
            cancellation.Dispose();
        }
    }

    private string? GetSelectedMetadataKindFilter() =>
        (this.FindControl<ComboBox>("MetadataSearchKindFilter")?.SelectedIndex ?? 0) switch
        {
            1 => "Tables", 2 => "Views", 3 => "Column", 4 => "Procedures",
            5 => "Triggers", 6 => "Indexes", 7 => "Domains", _ => null
        };

    private async void OnMetadataSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        UpdateMetadataReorderAvailability();
        if (!_isMetadataRefreshRunning &&
            _metadataTree.SelectedItem is DatabaseMetadataObject { Kind: "Category" } category &&
            !_metadataNextOffsets.ContainsKey(category.Name) && !_loadingMetadataCategories.Contains(category.Name))
        {
            await LoadMetadataPageAsync(category.Name, 0);
            return;
        }

        if (_metadataTree.SelectedItem is DatabaseMetadataObject { Kind: "Column", ParentName: not null } column)
        {
            var relationKind = GetOwningRelationKind(column.ParentName);
            await ShowTableDetailsAsync(new DatabaseMetadataObject(relationKind, column.ParentName, null,
                $"Opened from column search: {column.Name}"));
            return;
        }

        if (_metadataTree.SelectedItem is DatabaseMetadataObject { Kind: "Tables" or "Views" } table)
        {
            await ShowTableDetailsAsync(table);
            return;
        }

    }

    private async void OnConstraintDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (_tableConstraintsList.SelectedItem is DatabaseMetadataObject constraint &&
            !string.IsNullOrWhiteSpace(constraint.ParentName))
        {
            await ShowTableDetailsAsync(new DatabaseMetadataObject(
                "Tables", constraint.ParentName, null, $"Opened from constraint: {constraint.Name}"));
        }
    }

    private async void OnMetadataDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (_metadataTree.SelectedItem is not DatabaseMetadataObject item)
            return;

        await OpenMetadataObjectAsync(item);
    }

    private string GetOwningRelationKind(string? relationName)
    {
        if (string.IsNullOrWhiteSpace(relationName))
            return "Tables";

        var views = _metadataObjects.FirstOrDefault(category =>
            category.Kind == "Category" && category.Name.Equals("Views", StringComparison.OrdinalIgnoreCase));
        return views?.Children?.Any(view => view.Name.Equals(relationName, StringComparison.OrdinalIgnoreCase)) == true
            ? "Views"
            : "Tables";
    }

    private async Task OpenMetadataObjectAsync(DatabaseMetadataObject item)
    {
        switch (item.Kind)
        {
            case "Tables":
            case "Views":
                await ShowTableDetailsAsync(item);
                return;
            case "Column" when item.ParentName is not null:
            case "Constraint" when item.ParentName is not null:
            case "Primary key" when item.ParentName is not null:
            case "Foreign key" when item.ParentName is not null:
            case "Constraint column" when item.ParentName is not null:
            case "Constraint group" when item.ParentName is not null:
            case "View source" when item.ParentName is not null:
                var relationKind = item.Kind == "View source" ? "Views" : GetOwningRelationKind(item.ParentName);
                await ShowTableDetailsAsync(new DatabaseMetadataObject(
                    relationKind, item.ParentName, null, $"Opened from {item.Kind.ToLowerInvariant()}: {item.Name}"));
                return;
            case "Index segment" when item.ParentName is not null:
                await ShowMetadataObjectDetailsAsync(new DatabaseMetadataObject(
                    "Indexes", item.ParentName, null, $"Opened from index segment: {item.Name}"));
                return;
            case "Procedure parameter":
            case "Procedure parameter group":
            case "Procedure source":
                if (item.ParentName is not null)
                    await ShowMetadataObjectDetailsAsync(new DatabaseMetadataObject(
                        "Procedures", item.ParentName, null, $"Opened from {item.Kind.ToLowerInvariant()}: {item.Name}"));
                return;
            case "Trigger source":
                if (item.ParentName is not null)
                    await ShowMetadataObjectDetailsAsync(new DatabaseMetadataObject(
                        "Triggers", item.ParentName, null, "Opened from trigger source"));
                return;
            case "Domain properties":
            case "Domain default":
            case "Domain validation":
                if (item.ParentName is not null)
                    await ShowMetadataObjectDetailsAsync(new DatabaseMetadataObject(
                        "Domains", item.ParentName, null, $"Opened from {item.Kind.ToLowerInvariant()}: {item.Name}"));
                return;
            case "Procedures":
            case "Triggers":
            case "Indexes":
            case "Domains":
                await ShowMetadataObjectDetailsAsync(item);
                return;
        }
    }

    private async Task LoadMetadataObjectChildrenAsync(DatabaseMetadataObject item)
    {
        if (item.Children is not null || _connectionsList.SelectedItem is not ConnectionProfile profile)
            return;

        try
        {
            var details = await Task.Run(
                () => _sessionManager.GetMetadataObjectDetailsAsync(profile.Id, item.Kind, item.Name));
            if (_connectionsList.SelectedItem is not ConnectionProfile currentProfile || currentProfile.Id != profile.Id)
                return;
            var description = item.Details;
            if (item.Kind == "Procedures" && details.Count == 0)
                description += "; package-aware details unavailable for duplicate procedure name";
            else if (item.Kind == "Indexes")
                description += $"; {details.Count} segment(s)";
            else if (item.Kind == "Procedures")
                description += $"; {details.Count} detail group(s)";
            else if (item.Kind == "Triggers")
                description += "; source loaded";

            UpdateMetadataObjectNode(item, item with { Details = description, Children = details });
            _statusText.Text = $"Loaded {details.Count:N0} detail item(s) for {item.Kind.ToLowerInvariant()} '{item.Name}'.";
        }
        catch (Exception exception)
        {
            ReportError($"Loading metadata details for '{item.Kind}' object '{item.Name}'", exception);
        }
    }

    private void UpdateMetadataObjectNode(DatabaseMetadataObject original, DatabaseMetadataObject updated)
    {
        var category = _metadataObjects.FirstOrDefault(item => item.Kind == "Category" &&
            string.Equals(item.Name, original.Kind, StringComparison.OrdinalIgnoreCase));
        if (category?.Children is not ObservableCollection<DatabaseMetadataObject> children)
            return;
        var index = children.IndexOf(original);
        if (index >= 0) children[index] = updated;
    }

    private async Task LoadMetadataPageAsync(
        string categoryName,
        int offset,
        bool updateStatus = true,
        CancellationToken cancellationToken = default,
        int? loadGeneration = null)
    {
        if (!cancellationToken.CanBeCanceled && _metadataLoadingCancellation is not null)
            cancellationToken = _metadataLoadingCancellation.Token;
        var generation = loadGeneration ?? _metadataLoadGeneration;
        if (_connectionsList.SelectedItem is not ConnectionProfile profile ||
            string.IsNullOrWhiteSpace(categoryName) || !_loadingMetadataCategories.Add(categoryName))
            return;

        var pageCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _metadataPageCancellations[categoryName] = pageCancellation;
        var pageToken = pageCancellation.Token;

        var categoryIndex = _metadataObjects.ToList().FindIndex(item =>
            item.Kind == "Category" && string.Equals(item.Name, categoryName, StringComparison.OrdinalIgnoreCase));
        if (categoryIndex < 0)
        {
            _loadingMetadataCategories.Remove(categoryName);
            _metadataPageCancellations.Remove(categoryName);
            pageCancellation.Dispose();
            return;
        }

        if (updateStatus)
            _statusText.Text = offset == 0
                ? $"Loading {categoryName} metadata..."
                : $"Loading the next {MetadataPageSize} {categoryName.ToLowerInvariant()} objects; this may take some time...";
        try
        {
            // Some Firebird provider operations can still perform synchronous work internally.
            // Keep the entire catalog fetch off the UI thread so large catalogs never freeze
            // the window while each 200-object page is being read.
            var page = await Task.Run(
                () => _sessionManager.GetMetadataPageAsync(
                    profile.Id, categoryName, offset, MetadataPageSize, pageToken),
                pageToken);
            pageToken.ThrowIfCancellationRequested();
            if (generation != _metadataLoadGeneration ||
                _connectionsList.SelectedItem is not ConnectionProfile currentProfile || currentProfile.Id != profile.Id)
                return;
            categoryIndex = _metadataObjects.ToList().FindIndex(item =>
                item.Kind == "Category" && string.Equals(item.Name, categoryName, StringComparison.OrdinalIgnoreCase));
            if (categoryIndex < 0)
                return;

            var category = _metadataObjects[categoryIndex];
            if (category.Children is not BatchObservableCollection<DatabaseMetadataObject> children)
            {
                // Categories are initialized with observable children before paging begins.
                // Keep a defensive fallback for metadata providers that omit child collections.
                children = new BatchObservableCollection<DatabaseMetadataObject>();
                category = category with { Children = children };
                _metadataObjects[categoryIndex] = category;
            }
            else if (offset == 0)
            {
                children.Clear();
            }

            var pageItems = page.Where(item => item.Kind != "__LoadMore").ToArray();
            // Append in small ranges instead of raising one collection-change event per row.
            // Each range produces one UI notification, substantially reducing TreeView layout,
            // binding and container work while still yielding regularly to input/rendering.
            const int uiBatchSize = 50;
            for (var itemIndex = 0; itemIndex < pageItems.Length; itemIndex += uiBatchSize)
            {
                var batch = pageItems.Skip(itemIndex).Take(uiBatchSize).ToArray();
                pageToken.ThrowIfCancellationRequested();
                if (generation != _metadataLoadGeneration)
                    return;
                children.AddRange(batch);
                await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
                pageToken.ThrowIfCancellationRequested();
                if (generation != _metadataLoadGeneration ||
                    _connectionsList.SelectedItem is not ConnectionProfile activeProfile || activeProfile.Id != profile.Id)
                    return;
            }

            var displayed = pageItems.Length;
            var hasMore = page.Any(item => item.Kind == "__LoadMore");
            var nextOffset = offset + displayed;
            _metadataNextOffsets[categoryName] = nextOffset;
            if (hasMore && displayed > 0)
                _metadataCategoriesWithMore.Add(categoryName);
            else
            {
                _metadataCategoriesWithMore.Remove(categoryName);
            }

            if (updateStatus)
                _statusText.Text = hasMore
                    ? $"Loaded {displayed:N0} more {categoryName.ToLowerInvariant()} object(s). Scroll down to load more."
                    : $"Loaded all {nextOffset:N0} {categoryName.ToLowerInvariant()} object(s).";

            // If the current page fits in the visible tree area, continue filling it automatically.
            Dispatcher.UIThread.Post(TryLoadMoreMetadataWhenVisible);
        }
        catch (OperationCanceledException) when (
            pageToken.IsCancellationRequested || generation != _metadataLoadGeneration)
        {
            // The connection changed or a newer metadata refresh superseded this page.
        }
        catch (Exception exception)
        {
            if (generation == _metadataLoadGeneration)
                ReportError($"Loading {categoryName} metadata", exception);
        }
        finally
        {
            if (ReferenceEquals(_metadataPageCancellations.GetValueOrDefault(categoryName), pageCancellation))
                _metadataPageCancellations.Remove(categoryName);
            pageCancellation.Dispose();
            if (generation == _metadataLoadGeneration)
            {
                _loadingMetadataCategories.Remove(categoryName);
                UpdateMetadataReorderAvailability();
            }
        }
    }

    private void AttachMetadataTreeScrollViewer()
    {
        if (_metadataTreeScrollViewer is not null)
            return;

        _metadataTreeScrollViewer = _metadataTree.GetVisualDescendants()
            .OfType<ScrollViewer>()
            .FirstOrDefault();
        if (_metadataTreeScrollViewer is not null)
            _metadataTreeScrollViewer.ScrollChanged += (_, _) => TryLoadMoreMetadataWhenVisible();
    }

    private void TryLoadMoreMetadataWhenVisible()
    {
        if (_isMetadataRefreshRunning || _metadataTreeScrollViewer is not { } scrollViewer || _metadataCategoriesWithMore.Count == 0)
            return;

        var viewportHeight = scrollViewer.Viewport.Height;
        if (viewportHeight <= 0)
            return;

        // Virtualized TreeView panels expose only realized containers. Build this lookup
        // once per scroll event instead of walking the visual tree once per category.
        var visibleContainers = new Dictionary<DatabaseMetadataObject, TreeViewItem>(
            ReferenceEqualityComparer.Instance);
        foreach (var container in _metadataTree.GetVisualDescendants().OfType<TreeViewItem>())
        {
            if (container.DataContext is DatabaseMetadataObject item)
                visibleContainers.TryAdd(item, container);
        }

        foreach (var categoryName in _metadataCategoriesWithMore.ToArray())
        {
            if (_loadingMetadataCategories.Contains(categoryName))
                continue;

            var category = _metadataObjects.FirstOrDefault(item => item.Kind == "Category" &&
                string.Equals(item.Name, categoryName, StringComparison.OrdinalIgnoreCase));
            if (category?.Children is not ObservableCollection<DatabaseMetadataObject> children || children.Count == 0)
                continue;

            var lastItem = children[^1];
            if (!visibleContainers.TryGetValue(lastItem, out var lastContainer))
                continue;

            var itemBounds = lastContainer.GetTransformedBounds()?.Bounds;
            var viewportBounds = scrollViewer.GetTransformedBounds()?.Bounds;
            if (itemBounds is null || viewportBounds is null)
                continue;

            // Both bounds are expressed relative to the visual root, so no unsupported
            // TreeViewItem.TranslatePoint call is required.
            var top = itemBounds.Value.Top - viewportBounds.Value.Top;
            var bottom = itemBounds.Value.Bottom - viewportBounds.Value.Top;
            // Preload as soon as the last item enters the visible scroll viewport.
            if (bottom >= 0 && top <= viewportHeight)
            {
                var nextOffset = _metadataNextOffsets.GetValueOrDefault(categoryName);
                _ = LoadMetadataPageAsync(categoryName, nextOffset);
                return;
            }
        }
    }

    private void UpdateMetadataReorderAvailability()
    {
        if (_metadataTree.SelectedItem is not DatabaseMetadataObject { Kind: "Category" } selected)
        {
            _moveCategoryUpMenuItem.IsEnabled = false;
            _moveCategoryDownMenuItem.IsEnabled = false;
            return;
        }

        var index = _metadataObjects.IndexOf(selected);
        _moveCategoryUpMenuItem.IsEnabled = index > 0;
        _moveCategoryDownMenuItem.IsEnabled = index >= 0 && index < _metadataObjects.Count - 1;
    }

    private async void OnMoveCategoryUpClick(object? sender, RoutedEventArgs e) =>
        await MoveSelectedCategoryAsync(-1);

    private async void OnMoveCategoryDownClick(object? sender, RoutedEventArgs e) =>
        await MoveSelectedCategoryAsync(1);

    private async Task MoveSelectedCategoryAsync(int offset)
    {
        if (_metadataTree.SelectedItem is not DatabaseMetadataObject { Kind: "Category" } selected)
            return;

        var currentIndex = _metadataObjects.IndexOf(selected);
        var targetIndex = currentIndex + offset;
        if (currentIndex < 0 || targetIndex < 0 || targetIndex >= _metadataObjects.Count)
            return;

        _metadataObjects.Move(currentIndex, targetIndex);
        UpdateMetadataReorderAvailability();
        _preferences = _preferences with
        {
            MetadataCategoryOrder = _metadataObjects.Select(item => item.Name).ToArray()
        };
        await SavePreferencesAsync("Metadata category order saved.");
    }

    private void ClearMessages()
    {
        _messages.Clear();
        _statusText.Text = "Message log cleared.";
        _messagesScrollViewer.Offset = new Avalonia.Vector(0, 0);
    }

    private async Task ClearQueryHistoryAsync()
    {
        if (_isQueryExecuting)
        {
            ShowFooterNotification("Wait for the running query to finish before clearing query history.", "#9A7100");
            return;
        }

        try
        {
            await _queryHistoryStore.SaveAsync(Array.Empty<QueryHistoryEntry>());
            _queryHistory.Clear();
            _queryHistoryList.SelectedItem = null;
            _queryHistoryTabList.SelectedItem = null;
            _statusText.Text = "Query history cleared.";
            ShowFooterNotification("Query history cleared", "#397B45");
        }
        catch (Exception exception)
        {
            ReportError("Clearing query history", exception);
        }
    }

    private void ClearResults()
    {
        if (_isQueryExecuting)
        {
            ShowFooterNotification("Wait for the running query to finish before clearing results.", "#9A7100");
            return;
        }

        _queryResultsGrid.ItemsSource = null;
        _queryResultsGrid.Columns.Clear();
        _statusText.Text = "Query results cleared.";
        ShowFooterNotification("Query results cleared", "#397B45");
    }

    private void FormatActiveSql()
    {
        var editor = GetActiveSqlEditorTab().Editor;
        var selectionStart = editor.SelectionStart;
        var selectionLength = editor.SelectionLength;
        var hasSelection = selectionLength > 0;
        var source = hasSelection ? editor.SelectedText : editor.Document.Text;

        if (string.IsNullOrWhiteSpace(source))
        {
            ReportInformation("SQL formatting", "There is no SQL text to format.");
            return;
        }

        var formatted = FirebirdSqlFormatter.Format(source);
        if (string.Equals(source, formatted, StringComparison.Ordinal))
        {
            _statusText.Text = "SQL is already formatted.";
            return;
        }

        if (hasSelection)
        {
            editor.Document.Replace(selectionStart, selectionLength, formatted);
            editor.CaretOffset = Math.Min(selectionStart + formatted.Length, editor.Document.TextLength);
        }
        else
        {
            var previousCaretOffset = editor.CaretOffset;
            editor.Document.Replace(0, editor.Document.TextLength, formatted);
            editor.CaretOffset = Math.Min(previousCaretOffset, editor.Document.TextLength);
        }

        _editorTabs.SelectedItem = GetActiveSqlEditorTab().Tab;
        _statusText.Text = hasSelection
            ? "Selected SQL formatted."
            : "SQL editor content formatted.";
        ShowFooterNotification("SQL formatted", "#397B45");
    }

    private async void OnExecuteClick(object? sender, RoutedEventArgs e) => await ExecuteSelectedSqlAsync();

    private async Task ExecuteSelectedSqlAsync()
    {
        if (_connectionsList.SelectedItem is not ConnectionProfile profile ||
            !_sessionManager.GetActiveSessions().Any(session => session.ProfileId == profile.Id))
        {
            ReportError("SQL execution", "No active database connection. Select a saved connection profile and connect before executing SQL.");
            return;
        }

        string sql;
        try
        {
            sql = GetSqlToExecute();
        }
        catch (InvalidOperationException exception)
        {
            ReportError("SQL execution could not start", exception.Message);
            return;
        }

        if (string.IsNullOrWhiteSpace(sql))
        {
            ReportInformation("SQL execution", "There is no SQL statement to execute. Select a statement or enter SQL in the editor.");
            return;
        }

        var parameterNames = FirebirdSqlParameterParser.FindNames(sql);
        IReadOnlyDictionary<string, object?>? parameterValues = null;
        if (parameterNames.Count > 0)
        {
            parameterValues = await ShowSqlParametersDialogAsync(parameterNames);
            if (parameterValues is null)
            {
                _statusText.Text = "SQL execution cancelled; parameter values were not submitted.";
                return;
            }
        }

        var executionSql = FirebirdSqlParameterParser.NormalizePlaceholders(sql);
        var cancellation = new CancellationTokenSource();
        _queryExecutionCancellation = cancellation;
        _isQueryExecuting = true;
        _queryResultsGrid.ItemsSource = null;
        _queryResultsGrid.Columns.Clear();
        _statusText.Text = $"Executing SQL on '{profile.Name}'...";
        UpdateActionAvailability();
        var succeeded = false;
        var outcome = string.Empty;
        try
        {
            var result = await _sessionManager.ExecuteSqlAsync(
                profile.Id, executionSql, cancellation.Token, parameterValues);
            succeeded = true;
            if (SqlCompletionCatalog.MayChangeSchema(sql))
            {
                _completionMetadataCache.Remove(profile.Id);
                _completionDomainCache.Remove(profile.Id);
            }
            outcome = result.Columns.Count > 0
                ? $"{result.Rows.Count} row(s) returned in {result.Elapsed.TotalMilliseconds:N0} ms"
                : result.RowsAffected >= 0
                    ? $"{result.RowsAffected} row(s) affected in {result.Elapsed.TotalMilliseconds:N0} ms"
                    : $"Statement completed in {result.Elapsed.TotalMilliseconds:N0} ms";
            PopulateResultGrid(_queryResultsGrid, result.Columns, result.Rows);
            _statusText.Text = outcome;
            AddMessage("Success", "SQL execution", outcome);
            if (ReferenceEquals(_resultsMessagesTabs.SelectedItem, _messagesTab))
                _resultsMessagesTabs.SelectedItem = _resultsTab;
            ShowFooterNotification("Query executed successfully", "#397B45");
        }
        catch (OperationCanceledException)
        {
            outcome = "Execution cancelled.";
            _queryResultsGrid.ItemsSource = null;
            _queryResultsGrid.Columns.Clear();
            _statusText.Text = outcome;
            ReportInformation("SQL execution", outcome);
        }
        catch (Exception exception)
        {
            outcome = exception.Message;
            _queryResultsGrid.ItemsSource = null;
            _queryResultsGrid.Columns.Clear();
            ReportError("SQL execution (the statement transaction was rolled back when possible)", exception);
        }
        finally
        {
            _isQueryExecuting = false;
            _queryExecutionCancellation = null;
            cancellation.Dispose();
            var entry = new QueryHistoryEntry(
                DateTimeOffset.UtcNow, profile.Id, profile.Name, sql, succeeded, outcome);
            _queryHistory.Insert(0, entry);
            while (_queryHistory.Count > 250)
                _queryHistory.RemoveAt(_queryHistory.Count - 1);
            try
            {
                await _queryHistoryStore.SaveAsync(_queryHistory.ToArray());
            }
            catch (Exception historyException)
            {
                ReportError("Saving query history", historyException);
            }

            UpdateActionAvailability();
        }
    }

    private async Task<IReadOnlyDictionary<string, object?>?> ShowSqlParametersDialogAsync(IReadOnlyList<string> names)
    {
        var parameterTypes = new[] { "Text", "Integer", "Decimal", "Date", "Time", "Timestamp", "Boolean", "NULL" };
        var entries = new List<(string Name, TextBox Value, ComboBox Type)>();
        var fields = new StackPanel { Spacing = 8 };
        foreach (var name in names)
        {
            var value = new TextBox { PlaceholderText = $"Value for :{name}", MinWidth = 210 };
            var type = new ComboBox { ItemsSource = parameterTypes, SelectedIndex = 0, MinWidth = 115 };
            var row = new StackPanel
            {
                Orientation = Avalonia.Layout.Orientation.Horizontal,
                Spacing = 8,
                Children =
                {
                    new TextBlock { Text = $":{name}", Width = 130, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center },
                    value,
                    type
                }
            };
            fields.Children.Add(row);
            entries.Add((name, value, type));
        }

        var error = new TextBlock { Foreground = Avalonia.Media.Brushes.IndianRed, TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        var cancel = new Button { Content = "Cancel", MinWidth = 90 };
        var execute = new Button { Content = "Execute", MinWidth = 90 };
        var dialog = new Window
        {
            Title = "SQL Parameters",
            Width = 560,
            Height = Math.Min(620, 170 + names.Count * 48),
            MinHeight = 220,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = true,
            Content = new DockPanel
            {
                Margin = new Avalonia.Thickness(14),
                LastChildFill = true,
                Children =
                {
                    new StackPanel
                    {
                        Orientation = Avalonia.Layout.Orientation.Horizontal,
                        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                        Spacing = 8,
                        Children = { cancel, execute }
                    },
                    new StackPanel
                    {
                        Spacing = 10,
                        Children =
                        {
                            new TextBlock { Text = "Enter a value for each named SQL parameter. Choose its data type to bind it correctly.", TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                            new ScrollViewer { Content = fields, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto },
                            error
                        }
                    }
                }
            }
        };
        DockPanel.SetDock((StackPanel)((DockPanel)dialog.Content).Children[0], Dock.Bottom);
        cancel.Click += (_, _) => dialog.Close(null);
        execute.Click += (_, _) =>
        {
            try
            {
                var values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                foreach (var entry in entries)
                    values[entry.Name] = ParseSqlParameterValue(entry.Value.Text ?? string.Empty, entry.Type.SelectedItem?.ToString() ?? "Text");
                dialog.Close(values);
            }
            catch (Exception exception) when (exception is FormatException or OverflowException)
            {
                error.Text = $"Invalid parameter value: {exception.Message}";
            }
        };
        return await dialog.ShowDialog<Dictionary<string, object?>>(this);
    }

    private static object ParseSqlParameterValue(string value, string type) => type switch
    {
        "Integer" => long.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture),
        "Decimal" => decimal.Parse(value, NumberStyles.Number | NumberStyles.AllowExponent, CultureInfo.InvariantCulture),
        "Date" => DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces).Date,
        "Time" => TimeSpan.Parse(value, CultureInfo.InvariantCulture),
        "Timestamp" => DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces),
        "Boolean" => bool.Parse(value),
        "NULL" => DBNull.Value,
        _ => value
    };

    private static bool IsDdlSourceDetail(string kind) => kind is
        "Procedure source" or "Trigger source" or "View source" or "Domain default" or "Domain validation";

    private void OnCancelQueryClick(object? sender, RoutedEventArgs e)
    {
        if (_queryExecutionCancellation is { IsCancellationRequested: false } cancellation)
        {
            cancellation.Cancel();
            _statusText.Text = "Cancellation requested...";
        }
    }

    private void OnQueryHistoryDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is not ListBox historyList || historyList.SelectedItem is not QueryHistoryEntry entry)
            return;

        var editorTab = GetActiveSqlEditorTab();
        editorTab.Editor.Text = entry.Sql;
        _editorTabs.SelectedItem = editorTab.Tab;
        _statusText.Text = $"Restored query from {entry.ExecutedAt.ToLocalTime():g} ({entry.ProfileName}).";
    }

    private async Task SetThemeAsync(string theme)
    {
        if (theme is not ("Light" or "Dark"))
            return;

        _preferences = _preferences with { Theme = theme };
        ApplyTheme();
        await SavePreferencesAsync($"Theme set to {theme}.");
    }

    private void ApplyTheme()
    {
        Avalonia.Application.Current!.RequestedThemeVariant = _preferences.Theme switch
        {
            "Light" => ThemeVariant.Light,
            _ => ThemeVariant.Dark
        };

        foreach (var (_, editor) in _sqlEditors)
            editor.TextArea.TextView.Redraw();
        foreach (var editor in _readOnlySqlEditors)
            editor.TextArea.TextView.Redraw();
    }

    private static IDataTemplate CreateConnectionTemplate(bool showDetails) =>
        new FuncDataTemplate<ConnectionProfile>((profile, _) =>
        {
            if (profile is null)
                return new TextBlock();

            var content = new StackPanel { Spacing = 2, Margin = new Thickness(2, 4) };
            content.Children.Add(new TextBlock
            {
                Text = profile.Name ?? string.Empty,
                FontWeight = FontWeight.SemiBold
            });

            if (showDetails)
            {
                content.Children.Add(new TextBlock
                {
                    Text = profile.Host ?? string.Empty,
                    FontSize = 10,
                    Opacity = 0.7
                });
                content.Children.Add(new TextBlock
                {
                    Text = profile.Database ?? string.Empty,
                    FontSize = 10,
                    Opacity = 0.7,
                    TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis
                });
            }

            return content;
        });

    private void ApplyConnectionDisplayMode()
    {
        var showDetails = _preferences.ConnectionDisplayMode == "Detailed";
        _connectionsList.ItemTemplate = CreateConnectionTemplate(showDetails);
        this.FindControl<MenuItem>("ConnectionDisplayDetailedMenuItem")!.IsChecked = showDetails;
        this.FindControl<MenuItem>("ConnectionDisplayMinimalMenuItem")!.IsChecked = !showDetails;
        var toggleButton = this.FindControl<Button>("ToggleConnectionDisplayButton")!;
        toggleButton.Content = showDetails ? "Minimal" : "Detailed";
        ToolTip.SetTip(toggleButton, showDetails
            ? "Switch to minimal connection names"
            : "Switch to detailed connection information");
    }

    private async Task SetConnectionDisplayModeAsync(string mode)
    {
        if (mode is not ("Detailed" or "Minimal") ||
            mode == _preferences.ConnectionDisplayMode)
            return;

        var previousPreferences = _preferences;
        _preferences = _preferences with { ConnectionDisplayMode = mode };
        try
        {
            await _preferencesStore.SaveAsync(_preferences);
            ApplyConnectionDisplayMode();
            _statusText.Text = mode == "Detailed"
                ? "Detailed connection display enabled."
                : "Minimal connection display enabled.";
        }
        catch (Exception exception)
        {
            _preferences = previousPreferences;
            ApplyConnectionDisplayMode();
            ReportError("Saving connection display preference", exception);
        }
    }

    private void SetHistoryDockMode(bool dockRight)
    {
        _historyDockRightMenuItem.IsChecked = dockRight;
        _historyDockTabMenuItem.IsChecked = !dockRight;
        _queryHistoryPanel.IsVisible = dockRight;
        _historySplitter.IsVisible = dockRight;
        _queryHistoryTab.IsVisible = !dockRight;
        _workspaceGrid.ColumnDefinitions[1].Width = dockRight ? new GridLength(5) : new GridLength(0);
        _workspaceGrid.ColumnDefinitions[2].Width = dockRight
            ? new GridLength(300)
            : new GridLength(0);
    }

    private async void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.S && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            e.Handled = true;
            await SaveSqlEditorRecoveryNowAsync();
            return;
        }

        if (e.Key == Key.Space && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            e.Handled = true;
            await ShowSqlCompletionAsync();
            return;
        }

        if (e.Key == Key.F && e.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Shift))
        {
            e.Handled = true;
            FormatActiveSql();
            return;
        }

        if (e.Key != Key.F5 || e.KeyModifiers != KeyModifiers.None || _isQueryExecuting)
            return;

        e.Handled = true;
        await ExecuteSelectedSqlAsync();
    }

    private async Task ShowSqlCompletionAsync()
    {
        if (_connectionsList.SelectedItem is not ConnectionProfile profile ||
            !_sessionManager.GetActiveSessions().Any(session => session.ProfileId == profile.Id))
        {
            _statusText.Text = "Connect to a database before requesting SQL suggestions.";
            return;
        }

        try
        {
            var editor = GetActiveSqlEditorTab().Editor;
            var caretOffset = editor.CaretOffset;
            var documentText = editor.Document.Text;
            if (!_completionMetadataCache.TryGetValue(profile.Id, out var schema))
            {
                _statusText.Text = "Loading SQL completion metadata...";
                schema = await Task.Run(() => _sessionManager.GetSqlCompletionMetadataAsync(profile.Id));
                _completionMetadataCache[profile.Id] = schema;
            }

            IReadOnlyList<DatabaseMetadataObject> completionSchema = schema;
            var domainSearchDeferred = SqlCompletionCatalog.NeedsDomainSuggestions(documentText, caretOffset) &&
                SqlCompletionCatalog.GetPrefix(documentText, caretOffset).Length == 0;
            if (SqlCompletionCatalog.NeedsDomainSuggestions(documentText, caretOffset) && !domainSearchDeferred)
            {
                if (!_completionDomainCache.TryGetValue(profile.Id, out var domains))
                {
                    _statusText.Text = "Loading database domains for type completion...";
                    domains = await Task.Run(() => _sessionManager.GetSqlCompletionDomainsAsync(profile.Id));
                    _completionDomainCache[profile.Id] = domains;
                }
                completionSchema = schema.Concat(domains).ToArray();
            }

            if (_connectionsList.SelectedItem is not ConnectionProfile currentProfile || currentProfile.Id != profile.Id)
                return;

            var suggestions = SqlCompletionCatalog.Build(documentText, caretOffset, completionSchema);
            if (suggestions.Count == 0)
            {
                _statusText.Text = "No SQL suggestions match the current context.";
                return;
            }

            _completionWindow?.Close();
            var completionWindow = new CompletionWindow(editor.TextArea);
            _completionWindow = completionWindow;
            completionWindow.Closed += (_, _) =>
            {
                if (ReferenceEquals(_completionWindow, completionWindow)) _completionWindow = null;
            };
            foreach (var suggestion in suggestions)
                completionWindow.CompletionList.CompletionData.Add(new SqlCompletionData(suggestion));
            completionWindow.Show();
            _statusText.Text = $"{suggestions.Count:N0} SQL suggestion(s) · database: {profile.Name}" +
                (domainSearchDeferred ? " · type a prefix and press Ctrl+Space to search database domains" : string.Empty);
        }
        catch (Exception exception)
        {
            ReportError("SQL autocomplete", exception);
        }
    }

    private string GetSqlToExecute()
    {
        var editor = GetActiveSqlEditorTab().Editor;
        if (!string.IsNullOrWhiteSpace(editor.SelectedText))
        {
            var selectedSql = editor.SelectedText.Trim();
            EnsureMultipleStatementsAreSeparated(selectedSql);
            if (SplitSqlStatements(selectedSql).Count > 1)
                throw new InvalidOperationException("Select exactly one SQL statement to execute. Multiple statements must be executed separately.");
            return selectedSql;
        }

        var documentText = editor.Document.Text;
        EnsureMultipleStatementsAreSeparated(documentText);
        var statements = SplitSqlStatements(documentText);
        if (statements.Count > 1)
            throw new InvalidOperationException("This editor contains multiple SQL statements. Select the complete statement you want to execute. Separate statements with semicolons.");

        if (statements.Count == 0)
            return string.Empty;

        return statements[0].Trim();
    }


    private static void EnsureMultipleStatementsAreSeparated(string sql)
    {
        var statementStart = new Regex(
            @"\G(?:SELECT|INSERT|UPDATE|DELETE|MERGE|EXECUTE\s+(?:PROCEDURE|BLOCK|STATEMENT)|CREATE|ALTER|DROP|RECREATE|GRANT|REVOKE|COMMIT|ROLLBACK)\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
        var starts = 0;
        var depth = 0;
        var lineHasContent = false;
        var inSingleQuote = false;
        var inDoubleQuote = false;
        var inLineComment = false;
        var inBlockComment = false;

        for (var index = 0; index < sql.Length; index++)
        {
            var current = sql[index];
            var next = index + 1 < sql.Length ? sql[index + 1] : '\0';
            if (current is '\r' or '\n')
            {
                lineHasContent = false;
                if (inLineComment) inLineComment = false;
                continue;
            }
            if (inLineComment) continue;
            if (inBlockComment)
            {
                if (current == '*' && next == '/') { inBlockComment = false; index++; }
                continue;
            }
            if (inSingleQuote)
            {
                if (current == '\'' && next == '\'') { index++; continue; }
                if (current == '\'') inSingleQuote = false;
                continue;
            }
            if (inDoubleQuote)
            {
                if (current == '"' && next == '"') { index++; continue; }
                if (current == '"') inDoubleQuote = false;
                continue;
            }
            if (current == '-' && next == '-') { inLineComment = true; index++; continue; }
            if (current == '/' && next == '*') { inBlockComment = true; index++; continue; }
            if (current == '\'') { inSingleQuote = true; continue; }
            if (current == '"') { inDoubleQuote = true; continue; }

            if (!lineHasContent && char.IsWhiteSpace(current))
                continue;
            if (!lineHasContent && depth == 0 && statementStart.IsMatch(sql, index))
                starts++;
            lineHasContent = true;

            if (current == '(') depth++;
            else if (current == ')' && depth > 0) depth--;
        }

        if (starts > 1 && SplitSqlStatements(sql).Count <= 1)
            throw new InvalidOperationException("Multiple SQL statements must be separated with semicolons. Add a semicolon between statements, then select the statement you want to execute.");
    }

    private static IReadOnlyList<string> SplitSqlStatements(string sql)
    {
        var statements = new List<string>();
        var start = 0;
        var inSingleQuote = false;
        var inDoubleQuote = false;
        var inLineComment = false;
        var inBlockComment = false;

        for (var index = 0; index < sql.Length; index++)
        {
            var current = sql[index];
            var next = index + 1 < sql.Length ? sql[index + 1] : '\0';

            if (inLineComment)
            {
                if (current is '\r' or '\n') inLineComment = false;
                continue;
            }
            if (inBlockComment)
            {
                if (current == '*' && next == '/') { inBlockComment = false; index++; }
                continue;
            }
            if (inSingleQuote)
            {
                if (current == '\'' && next == '\'') { index++; continue; }
                if (current == '\'') inSingleQuote = false;
                continue;
            }
            if (inDoubleQuote)
            {
                if (current == '"' && next == '"') { index++; continue; }
                if (current == '"') inDoubleQuote = false;
                continue;
            }

            if (current == '-' && next == '-') { inLineComment = true; index++; continue; }
            if (current == '/' && next == '*') { inBlockComment = true; index++; continue; }
            if (current == '\'') { inSingleQuote = true; continue; }
            if (current == '"') { inDoubleQuote = true; continue; }
            if (current != ';') continue;

            AddStatement(sql[start..index], statements);
            start = index + 1;
        }

        AddStatement(sql[start..], statements);
        return statements;
    }

    private static void AddStatement(string candidate, ICollection<string> statements)
    {
        var withoutComments = Regex.Replace(candidate, @"/\*.*?\*/|--[^\r\n]*", " ", RegexOptions.Singleline);
        if (!string.IsNullOrWhiteSpace(withoutComments))
            statements.Add(candidate);
    }

    private (TabItem Tab, AvaloniaEdit.TextEditor Editor) GetActiveSqlEditorTab()
    {
        if (_editorTabs.SelectedItem is TabItem selected)
        {
            var active = _sqlEditors.FirstOrDefault(item => ReferenceEquals(item.Tab, selected));
            if (active.Editor is not null)
                return active;
        }

        return _sqlEditors[0];
    }

    private void ConfigureSqlEditorTab(TabItem tab, AvaloniaEdit.TextEditor editor)
    {
        tab.Tag = editor;
        _sqlEditors.Add((tab, editor));
        editor.Document.TextChanged += (_, _) =>
        {
            if (!_isRestoringEditors)
                ScheduleSqlEditorRecoverySave();
        };
        var renameItem = new MenuItem { Header = "Rename editor…" };
        renameItem.Click += async (_, _) => await RenameSqlEditorTabAsync(tab);
        var closeItem = new MenuItem { Header = "Close tab" };
        closeItem.Click += async (_, _) => await CloseSqlEditorTabAsync(tab);
        tab.ContextMenu = new ContextMenu { Items = { renameItem, closeItem } };
        EnableMiddleClickClose(tab, () => CloseSqlEditorTabAsync(tab));
        editor.TextArea.TextView.AddHandler(InputElement.PointerPressedEvent, async (_, args) =>
        {
            if (!args.KeyModifiers.HasFlag(KeyModifiers.Control))
                return;

            var pointerPosition = args.GetPosition(editor.TextArea.TextView);
            var position = editor.TextArea.TextView.GetPosition(pointerPosition);
            if (position is null)
                return;

            var offset = editor.Document.GetOffset(position.Value.Location);
            await OpenSqlIdentifierAsync(editor, offset);
        }, RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    private void CreateSqlEditorTab()
    {
        var number = _sqlEditors.Count + 1;
        var editor = new AvaloniaEdit.TextEditor
        {
            ShowLineNumbers = true,
            FontFamily = new FontFamily("Cascadia Code, Consolas, monospace"),
            FontSize = 13,
            WordWrap = false,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
        };
        editor.TextArea.TextView.LineTransformers.Add(new FirebirdSqlSyntaxColorizer(() => _preferences.Theme == "Dark"));

        var content = new Grid();
        content.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        content.RowDefinitions.Add(new RowDefinition(GridLength.Star));
        var hint = new Border
        {
            Padding = new Avalonia.Thickness(7),
            BorderThickness = new Avalonia.Thickness(0, 0, 0, 1),
            Child = new TextBlock
            {
                Text = "F5 executes the selected SQL, or the only statement in this editor. Select a statement when multiple statements are present.",
                FontSize = 10,
                Opacity = 0.65
            }
        };
        content.Children.Add(hint);
        var editorHost = new Grid();
        Grid.SetRow(editorHost, 1);
        editorHost.Children.Add(editor);
        content.Children.Add(editorHost);

        var tab = new TabItem { Header = $"SQL Editor {number}", Content = content };
        ConfigureSqlEditorTab(tab, editor);
        var insertIndex = _sqlEditors.Count - 1;
        _editorTabs.Items.Insert(insertIndex, tab);
        _editorTabs.SelectedItem = tab;
        editor.Focus();
        ScheduleSqlEditorRecoverySave(showNotification: false);
    }

    private async Task RenameSqlEditorTabAsync(TabItem tab)
    {
        var currentName = tab.Header?.ToString() ?? "SQL Editor";
        var nameInput = new TextBox { Text = currentName, Margin = new Avalonia.Thickness(0, 8, 0, 12) };
        var renameButton = new Button { Content = "Rename", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right, MinWidth = 90 };
        var dialog = new Window
        {
            Title = "Rename SQL Editor",
            Width = 360,
            Height = 145,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            Content = new StackPanel
            {
                Margin = new Avalonia.Thickness(14),
                Children = { new TextBlock { Text = "Editor title" }, nameInput, renameButton }
            }
        };
        renameButton.Click += (_, _) => dialog.Close(true);
        var accepted = await dialog.ShowDialog<bool>(this);
        if (accepted && !string.IsNullOrWhiteSpace(nameInput.Text))
        {
            tab.Header = nameInput.Text.Trim();
            ScheduleSqlEditorRecoverySave(showNotification: false);
        }
    }

    private async Task CloseSqlEditorTabAsync(TabItem tab)
    {
        var editorIndex = _sqlEditors.FindIndex(item => ReferenceEquals(item.Tab, tab));
        if (editorIndex < 0)
            return;

        var editor = _sqlEditors[editorIndex].Editor;
        if (!string.IsNullOrWhiteSpace(editor.Text) && !await ConfirmCloseSqlEditorTabAsync(tab))
            return;

        _sqlEditors.RemoveAt(editorIndex);
        _editorTabs.Items.Remove(tab);

        if (_sqlEditors.Count == 0)
        {
            // Keep one usable editor available, but do not retain the closed tab's content.
            CreateSqlEditorTab();
        }
        else
        {
            var nextIndex = Math.Min(editorIndex, _sqlEditors.Count - 1);
            _editorTabs.SelectedItem = _sqlEditors[nextIndex].Tab;
        }

        await SaveSqlEditorRecoveryNowAsync(confirmation: null);
    }

    private async Task<bool> ConfirmCloseSqlEditorTabAsync(TabItem tab)
    {
        var cancelButton = new Button { Content = "Cancel", MinWidth = 90 };
        var closeButton = new Button { Content = "Close editor", MinWidth = 100 };
        var dialog = new Window
        {
            Title = "Close SQL Editor",
            Width = 390,
            Height = 165,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            Content = new StackPanel
            {
                Margin = new Avalonia.Thickness(16),
                Spacing = 12,
                Children =
                {
                    new TextBlock
                    {
                        Text = $"'{tab.Header}' contains SQL text. Close this editor and delete its saved contents?",
                        TextWrapping = Avalonia.Media.TextWrapping.Wrap
                    },
                    new StackPanel
                    {
                        Orientation = Avalonia.Layout.Orientation.Horizontal,
                        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                        Spacing = 8,
                        Children = { cancelButton, closeButton }
                    }
                }
            }
        };
        cancelButton.Click += (_, _) => dialog.Close(false);
        closeButton.Click += (_, _) => dialog.Close(true);
        return await dialog.ShowDialog<bool>(this);
    }

    private void EnableMiddleClickClose(TabItem tab, Func<Task> closeAction)
    {
        tab.AddHandler(InputElement.PointerPressedEvent, async (_, args) =>
        {
            if (args.GetCurrentPoint(tab).Properties.PointerUpdateKind != PointerUpdateKind.MiddleButtonPressed)
                return;

            args.Handled = true;
            await closeAction();
        }, RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    private static void PopulateResultGrid(
        TableView grid,
        IReadOnlyList<string> columns,
        IReadOnlyList<IReadOnlyList<string>> rows)
    {
        grid.Columns.Clear();
        for (var index = 0; index < columns.Count; index++)
        {
            grid.Columns.Add(new TableViewColumn
            {
                Header = columns[index],
                Binding = new Binding($"Values[{index}]"),
                Width = new GridLength(160)
            });
        }

        grid.ItemsSource = rows.Select(row => new QueryResultRow(row.ToArray())).ToArray();
    }

    private async Task OpenSqlIdentifierAsync(AvaloniaEdit.TextEditor editor, int offset)
    {
        if (_connectionsList.SelectedItem is not ConnectionProfile profile ||
            !_sessionManager.GetActiveSessions().Any(session => session.ProfileId == profile.Id))
        {
            ShowFooterNotification("Connect to a database to navigate database objects.", "#B7791F");
            return;
        }

        var sqlText = editor.Text ?? string.Empty;
        if (!IsSqlIdentifierContext(sqlText, offset))
            return;

        var identifier = GetSqlIdentifierAtOffset(sqlText, offset);
        if (string.IsNullOrWhiteSpace(identifier))
            return;

        try
        {
            if (!_completionMetadataCache.TryGetValue(profile.Id, out var metadata))
            {
                metadata = await _sessionManager.GetSqlCompletionMetadataAsync(profile.Id);
                _completionMetadataCache[profile.Id] = metadata;
            }

            var matches = metadata.Where(item =>
                string.Equals(item.Name, identifier, StringComparison.OrdinalIgnoreCase)).ToArray();
            var relation = matches.FirstOrDefault(item => item.Kind is "Table" or "View");
            if (relation is not null)
            {
                await ShowTableDetailsAsync(new DatabaseMetadataObject(
                    relation.Kind == "View" ? "Views" : "Tables", relation.Name, null, "Opened from SQL Editor"));
                return;
            }

            var databaseObject = matches.FirstOrDefault(item => item.Kind is "Procedure" or "Trigger" or "Index" or "Sequence" or "Exception" or "Function");
            if (databaseObject is not null)
            {
                var kind = databaseObject.Kind switch
                {
                    "Procedure" => "Procedures",
                    "Trigger" => "Triggers",
                    "Index" => "Indexes",
                    _ => databaseObject.Kind + "s"
                };
                await ShowMetadataObjectDetailsAsync(new DatabaseMetadataObject(
                    kind, databaseObject.Name, null, $"{databaseObject.Kind} opened from SQL Editor"));
                return;
            }

            var fieldMatches = metadata
                .Where(item => item.Kind is "Table" or "View")
                .SelectMany(item => (item.Children ?? Array.Empty<DatabaseMetadataObject>())
                    .Where(field => string.Equals(field.Name, identifier, StringComparison.OrdinalIgnoreCase))
                    .Select(_ => item))
                .DistinctBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (fieldMatches.Length == 1)
            {
                var owner = fieldMatches[0];
                await ShowTableDetailsAsync(new DatabaseMetadataObject(
                    owner.Kind == "View" ? "Views" : "Tables", owner.Name, null,
                    $"Opened from SQL field '{identifier}'"));
            }
        }
        catch (Exception exception)
        {
            ReportError($"Navigating to database object '{identifier}'", exception);
        }
    }

    private static bool IsSqlIdentifierContext(string sql, int offset)
    {
        var inSingleQuote = false;
        var inDoubleQuote = false;
        var inLineComment = false;
        var inBlockComment = false;

        for (var index = 0; index < Math.Min(offset, sql.Length); index++)
        {
            var current = sql[index];
            var next = index + 1 < sql.Length ? sql[index + 1] : '\0';

            if (inLineComment)
            {
                if (current is '\r' or '\n') inLineComment = false;
                continue;
            }
            if (inBlockComment)
            {
                if (current == '*' && next == '/') { inBlockComment = false; index++; }
                continue;
            }
            if (inSingleQuote)
            {
                if (current == '\'' && next == '\'') { index++; continue; }
                if (current == '\'') inSingleQuote = false;
                continue;
            }
            if (inDoubleQuote)
            {
                if (current == '"' && next == '"') { index++; continue; }
                if (current == '"') inDoubleQuote = false;
                continue;
            }

            if (current == '-' && next == '-') { inLineComment = true; index++; continue; }
            if (current == '/' && next == '*') { inBlockComment = true; index++; continue; }
            if (current == '\'') inSingleQuote = true;
            else if (current == '"') inDoubleQuote = true;
        }

        return !inSingleQuote && !inLineComment && !inBlockComment;
    }

    private static string? GetSqlIdentifierAtOffset(string sql, int offset)
    {
        if (string.IsNullOrEmpty(sql) || offset < 0 || offset > sql.Length)
            return null;

        var index = Math.Min(offset, sql.Length - 1);
        var inQuotedIdentifier = false;
        var quotedIdentifierStart = -1;
        for (var cursor = 0; cursor <= index; cursor++)
        {
            if (sql[cursor] != '"')
                continue;

            if (inQuotedIdentifier && cursor + 1 < sql.Length && sql[cursor + 1] == '"')
            {
                cursor++;
                continue;
            }

            if (!inQuotedIdentifier)
            {
                inQuotedIdentifier = true;
                quotedIdentifierStart = cursor;
                continue;
            }

            if (index <= cursor)
                return sql[(quotedIdentifierStart + 1)..cursor].Replace("\"\"", "\"", StringComparison.Ordinal);

            inQuotedIdentifier = false;
            quotedIdentifierStart = -1;
        }

        if (inQuotedIdentifier && quotedIdentifierStart >= 0)
        {
            var cursor = index + 1;
            while (cursor < sql.Length)
            {
                if (sql[cursor] == '"' && cursor + 1 < sql.Length && sql[cursor + 1] == '"')
                {
                    cursor += 2;
                    continue;
                }
                if (sql[cursor] == '"')
                    return sql[(quotedIdentifierStart + 1)..cursor].Replace("\"\"", "\"", StringComparison.Ordinal);
                cursor++;
            }
        }

        static bool IsIdentifierCharacter(char character) =>
            char.IsLetterOrDigit(character) || character is '_' or '$' or '#';

        if (!IsIdentifierCharacter(sql[index]) && index > 0 && IsIdentifierCharacter(sql[index - 1]))
            index--;
        if (!IsIdentifierCharacter(sql[index]))
            return null;

        var start = index;
        var endIndex = index;
        while (start > 0 && IsIdentifierCharacter(sql[start - 1])) start--;
        while (endIndex + 1 < sql.Length && IsIdentifierCharacter(sql[endIndex + 1])) endIndex++;
        return sql[start..(endIndex + 1)];
    }

    private AvaloniaEdit.TextEditor CreateReadOnlySqlEditor(string? source, double minHeight = 180)
    {
        var editor = new AvaloniaEdit.TextEditor
        {
            Text = FirebirdSqlFormatter.Format(source),
            IsReadOnly = true,
            ShowLineNumbers = true,
            WordWrap = false,
            FontSize = 11,
            FontFamily = new FontFamily("Cascadia Code, Consolas, monospace"),
            MinHeight = minHeight,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
        };
        editor.TextArea.TextView.LineTransformers.Add(
            new FirebirdSqlSyntaxColorizer(() => _preferences.Theme == "Dark"));
        _readOnlySqlEditors.Add(editor);
        return editor;
    }

    private async Task ShowMetadataObjectDetailsAsync(DatabaseMetadataObject item)
    {
        if (_connectionsList.SelectedItem is not ConnectionProfile profile ||
            !_sessionManager.GetActiveSessions().Any(session => session.ProfileId == profile.Id))
            return;

        var existingTab = _editorTabs.Items.OfType<TabItem>().FirstOrDefault(tab =>
            tab.Tag is MetadataObjectTabIdentity identity &&
            identity.ProfileId == profile.Id &&
            identity.Kind.Equals(item.Kind, StringComparison.OrdinalIgnoreCase) &&
            identity.Name.Equals(item.Name, StringComparison.OrdinalIgnoreCase));
        if (existingTab is not null)
        {
            _editorTabs.SelectedItem = existingTab;
            return;
        }

        try
        {
            var details = await Task.Run(
                () => _sessionManager.GetMetadataObjectDetailsAsync(profile.Id, item.Kind, item.Name));
            var panel = new StackPanel { Spacing = 8, Margin = new Avalonia.Thickness(12) };
            panel.Children.Add(new TextBlock
            {
                Text = $"{GetMetadataDisplayKind(item.Kind)}: {item.Name}",
                FontSize = 15,
                FontWeight = FontWeight.SemiBold
            });
            if (!string.IsNullOrWhiteSpace(item.Details))
                panel.Children.Add(new TextBlock { Text = item.Details, Opacity = 0.75, TextWrapping = Avalonia.Media.TextWrapping.Wrap });

            if (details.Count == 0)
            {
                panel.Children.Add(new TextBlock
                {
                    Text = item.Kind == "Procedures"
                        ? "No details are available. The procedure name may be ambiguous because of package metadata."
                        : "No additional metadata is available for this object.",
                    Opacity = 0.75,
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap
                });
            }

            foreach (var detail in details)
            {
                panel.Children.Add(new TextBlock
                {
                    Text = detail.Name,
                    FontWeight = FontWeight.SemiBold,
                    Margin = new Avalonia.Thickness(0, 6, 0, 0)
                });
                if (IsDdlSourceDetail(detail.Kind))
                {
                    panel.Children.Add(new ScrollViewer
                    {
                        MaxHeight = 400,
                        HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                        VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                        Content = CreateReadOnlySqlEditor(detail.Details, 220)
                    });
                }
                else
                {
                    panel.Children.Add(new TextBlock
                    {
                        Text = IsDdlSourceDetail(detail.Kind)
                            ? FirebirdSqlFormatter.Format(detail.Details)
                            : detail.Details,
                        TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                        Opacity = 0.85
                    });
                    if (detail.Children is not null)
                    {
                        foreach (var child in detail.Children)
                            panel.Children.Add(new TextBlock
                            {
                                Text = $"{child.Name}  ·  {child.Details}",
                                Margin = new Avalonia.Thickness(12, 2, 0, 2),
                                TextWrapping = Avalonia.Media.TextWrapping.Wrap
                            });
                    }
                }
            }

            var tab = new TabItem
            {
                Header = $"{GetMetadataDisplayKind(item.Kind)}: {item.Name}",
                Tag = new MetadataObjectTabIdentity(profile.Id, item.Kind, item.Name),
                Content = new ScrollViewer { Content = panel }
            };
            var closeItem = new MenuItem { Header = "Close tab" };
            closeItem.Click += (_, _) => CloseMetadataObjectTab(tab);
            tab.ContextMenu = new ContextMenu { Items = { closeItem } };
            EnableMiddleClickClose(tab, () => { CloseMetadataObjectTab(tab); return Task.CompletedTask; });
            var insertIndex = _sqlEditors.Count + 1;
            _editorTabs.Items.Insert(insertIndex, tab);
            _editorTabs.SelectedItem = tab;
        }
        catch (Exception exception)
        {
            ReportError($"Opening {item.Kind} object '{item.Name}'", exception);
        }
    }

    private void CloseMetadataObjectTab(TabItem tab)
    {
        if (!_editorTabs.Items.Contains(tab))
            return;
        var wasSelected = ReferenceEquals(_editorTabs.SelectedItem, tab);
        _editorTabs.Items.Remove(tab);
        if (wasSelected)
            _editorTabs.SelectedItem = _sqlEditors[^1].Tab;
    }

    private void CloseTableDetailsTab()
    {
        _tableDetailsTab.IsVisible = false;
        _currentTableDetailsName = null;
        if (ReferenceEquals(_editorTabs.SelectedItem, _tableDetailsTab))
            _editorTabs.SelectedItem = _sqlEditors[0].Tab;
    }

    private static string GetMetadataDisplayKind(string kind) => kind switch
    {
        "Procedures" => "Procedure",
        "Triggers" => "Trigger",
        "Indexes" => "Index",
        "Domains" => "Domain",
        _ => kind
    };

    private sealed record MetadataObjectTabIdentity(Guid ProfileId, string Kind, string Name);

    private async Task ShowTableDetailsAsync(DatabaseMetadataObject table)
    {
        if (_connectionsList.SelectedItem is not ConnectionProfile profile ||
            !_sessionManager.GetActiveSessions().Any(session => session.ProfileId == profile.Id))
            return;

        if (string.Equals(_currentTableDetailsName, table.Name, StringComparison.Ordinal) &&
            _tableDetailsTab.IsVisible)
        {
            _editorTabs.SelectedItem = _tableDetailsTab;
            return;
        }

        _tableDetailsTab.Header = $"{(table.Kind == "Views" ? "View" : "Table")}: {table.Name}";
        _tableDetailsTab.IsVisible = true;
        _editorTabs.SelectedItem = _tableDetailsTab;
        _tableDetailsTitle.Text = $"{table.Name}  ·  Loading object details...";
        _currentTableDetailsName = table.Name;
        _currentTableDataOffset = 0;
        _currentTableDataPage = null;
        _tableFieldsGrid.ItemsSource = null;
        _tableConstraintsList.ItemsSource = null;
        _tableDdlText.Text = string.Empty;
        _tableDataGrid.ItemsSource = null;
        _tableDataGrid.Columns.Clear();
        _previousTableDataPageButton.IsEnabled = false;
        _nextTableDataPageButton.IsEnabled = false;
        _tableDataPageStatus.Text = "Loading...";

        try
        {
            var details = await _sessionManager.GetMetadataObjectDetailsAsync(profile.Id, table.Kind, table.Name);
            if (!string.Equals(_currentTableDetailsName, table.Name, StringComparison.Ordinal) ||
                _connectionsList.SelectedItem is not ConnectionProfile currentProfile || currentProfile.Id != profile.Id)
                return;

            UpdateMetadataObjectNode(table, table with { Children = details });
            var fields = details.Where(item => item.Kind == "Column").ToArray();
            var fieldRows = fields.Select(item => new TableFieldDisplay(
                item.Name,
                ReadDetail(item.Details, "Domain"),
                DescribeFieldType(item.Details),
                item.Details.Contains("not null", StringComparison.OrdinalIgnoreCase) ? "No" : "Yes",
                ReadDetail(item.Details, "position") ?? "?")).ToArray();
            _tableFieldsGrid.ItemsSource = fieldRows;
            var constraints = details.FirstOrDefault(item => item.Kind == "Constraint group")?.Children
                ?? Array.Empty<DatabaseMetadataObject>();
            _tableConstraintsList.ItemsSource = constraints;
            _tableDdlText.Text = table.Kind == "Tables"
                ? FirebirdSqlFormatter.Format(BuildTableDdl(table.Name, fields, constraints))
                : FirebirdSqlFormatter.Format(details.FirstOrDefault(item => item.Kind == "View source")?.Details
                    ?? "-- View definition is not available.");
            _tableDdlText.TextArea.TextView.Redraw();
            _tableDetailsTitle.Text = $"{table.Name}  ·  {fields.Length} column(s) · {constraints.Count} constraint(s)";
            await LoadTableDataPageAsync(profile, table.Name, 0, null, false);
        }
        catch (Exception exception)
        {
            ReportError($"Loading table details for '{table.Name}'", exception);
            _tableDataPageStatus.Text = "Load failed";
        }
    }

    private async Task ChangeTableDataPageAsync(int direction)
    {
        if (_connectionsList.SelectedItem is not ConnectionProfile profile || string.IsNullOrWhiteSpace(_currentTableDetailsName))
            return;
        var nextOffset = direction < 0 ? Math.Max(0, _currentTableDataOffset - TableDataPageSize) :
            checked(_currentTableDataOffset + TableDataPageSize);
        IReadOnlyList<object?>? cursorValues = null;
        var previous = direction < 0;
        if (_currentTableDataPage?.HasStableOrder == true)
        {
            cursorValues = previous
                ? _currentTableDataPage.FirstKeyValues
                : _currentTableDataPage.LastKeyValues;
            if (cursorValues is null) return;
        }
        await LoadTableDataPageAsync(profile, _currentTableDetailsName, nextOffset, cursorValues, previous);
    }

    private async Task LoadTableDataPageAsync(
        ConnectionProfile profile, string tableName, int offset,
        IReadOnlyList<object?>? cursorValues, bool previous)
    {
        _previousTableDataPageButton.IsEnabled = false;
        _nextTableDataPageButton.IsEnabled = false;
        _tableDataPageStatus.Text = $"Loading rows {offset + 1:N0}–{offset + TableDataPageSize:N0}...";
        try
        {
            var page = await _sessionManager.GetTableDataPageAsync(
                profile.Id, tableName, offset, TableDataPageSize, cursorValues, previous);
            if (!string.Equals(_currentTableDetailsName, tableName, StringComparison.Ordinal) ||
                _connectionsList.SelectedItem is not ConnectionProfile currentProfile || currentProfile.Id != profile.Id)
                return;

            PopulateResultGrid(_tableDataGrid, page.Columns, page.Rows);
            _currentTableDataOffset = offset;
            _currentTableDataPage = page;
            _previousTableDataPageButton.IsEnabled = page.HasPrevious;
            _nextTableDataPageButton.IsEnabled = page.HasNext;
            var first = page.Rows.Count == 0 ? 0 : offset + 1;
            var last = offset + page.Rows.Count;
            _tableDataPageStatus.Text = page.Rows.Count == 0
                ? "No rows on this page"
                : $"Rows {first:N0}–{last:N0} · page size {TableDataPageSize}" +
                    (page.HasStableOrder ? " · primary-key seek" : " · offset (no primary key)") +
                    (page.HasNext ? " · more available" : " · end reached");
            _statusText.Text = $"Loaded {page.Rows.Count:N0} row(s) from '{tableName}' (offset {offset:N0}).";
        }
        catch (Exception exception)
        {
            _tableDataPageStatus.Text = "Load failed";
            ReportError($"Loading table data for '{tableName}'", exception);
        }
    }

    private static string? ReadDetail(string details, string key)
    {
        foreach (var part in details.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = part.IndexOf(':');
            if (separator <= 0)
                continue;
            if (string.Equals(part[..separator].Trim(), key, StringComparison.OrdinalIgnoreCase))
                return part[(separator + 1)..].Trim();
        }

        return null;
    }

    private static string DescribeFieldType(string details)
    {
        var typeCode = ReadDetail(details, "type code");
        var subtype = ReadDetail(details, "subtype") ?? "0";
        var length = ReadDetail(details, "length") ?? "?";
        var characterLength = ReadDetail(details, "character length");
        var precision = int.TryParse(ReadDetail(details, "precision"), out var parsedPrecision)
            ? parsedPrecision
            : 0;
        var scale = int.TryParse(ReadDetail(details, "scale"), out var parsedScale) ? Math.Abs(parsedScale) : 0;
        return typeCode switch
        {
            "7" when subtype is "1" or "2" => $"{(subtype == "2" ? "DECIMAL" : "NUMERIC")}({(precision > 0 ? precision : 4)},{scale})",
            "7" => "SMALLINT",
            "8" when subtype is "1" or "2" => $"{(subtype == "2" ? "DECIMAL" : "NUMERIC")}({(precision > 0 ? precision : 9)},{scale})",
            "8" => "INTEGER",
            "9" => "BIGINT",
            "10" => "FLOAT",
            "11" => "D_FLOAT",
            "12" => "DATE",
            "13" => "TIME",
            "14" => $"CHAR({characterLength ?? length})",
            "16" when subtype is "1" or "2" => $"{(subtype == "2" ? "DECIMAL" : "NUMERIC")}({(precision > 0 ? precision : 18)},{scale})",
            "16" => "BIGINT",
            "23" => "BOOLEAN",
            "24" => "DECFLOAT(16)",
            "25" => "DECFLOAT(34)",
            "26" => "INT128",
            "27" => "DOUBLE PRECISION",
            "28" => "TIME WITH TIME ZONE",
            "29" => "TIMESTAMP WITH TIME ZONE",
            "35" => "TIMESTAMP",
            "37" => $"VARCHAR({characterLength ?? length})",
            "40" => $"CSTRING({length})",
            "261" => subtype == "1" ? "BLOB SUB_TYPE TEXT" : "BLOB",
            _ => $"TYPE {typeCode ?? "?"} (length {length})"
        };
    }

    
    private static string QuoteIdentifier(string identifier) =>
        "\"" + identifier.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";

    private int GetCategoryOrderIndex(string category)
    {
        for (var index = 0; index < _preferences.MetadataCategoryOrder.Count; index++)
        {
            if (string.Equals(_preferences.MetadataCategoryOrder[index], category, StringComparison.OrdinalIgnoreCase))
                return index;
        }

        return int.MaxValue;
    }

    private async Task SavePreferencesAsync(string successMessage)
    {
        try
        {
            await _preferencesStore.SaveAsync(_preferences);
            _statusText.Text = successMessage;
        }
        catch (Exception exception)
        {
            ReportError("Saving application preferences (the setting remains active for this session)", exception);
        }
    }

    private void ScheduleSqlEditorRecoverySave(bool showNotification = true)
    {
        _recoverySaveCancellation?.Cancel();
        var cancellation = new CancellationTokenSource();
        _recoverySaveCancellation = cancellation;
        _recoverySaveTask = SaveSqlEditorRecoveryAfterDelayAsync(cancellation, showNotification);
    }

    private async Task SaveSqlEditorRecoveryAfterDelayAsync(
        CancellationTokenSource cancellation,
        bool showNotification)
    {
        var cancellationToken = cancellation.Token;
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(700), cancellationToken);
            await PersistSqlEditorRecoveryAsync(
                cancellationToken,
                showNotification ? "Editor autosaved" : null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            if (IsVisible)
                ReportError("Saving SQL editor recovery data", exception);
        }
        finally
        {
            if (ReferenceEquals(_recoverySaveCancellation, cancellation))
                _recoverySaveCancellation = null;
            cancellation.Dispose();
        }
    }

    private async Task SaveSqlEditorRecoveryNowAsync(string? confirmation = "Editor saved")
    {
        var pendingSave = _recoverySaveTask;
        _recoverySaveCancellation?.Cancel();
        try
        {
            await pendingSave;
            await PersistSqlEditorRecoveryAsync(CancellationToken.None, confirmation);
        }
        catch (Exception exception)
        {
            ReportError("Saving SQL editor recovery data", exception);
        }
    }

    private async Task PersistSqlEditorRecoveryAsync(CancellationToken cancellationToken, string? confirmation)
    {
        var activeIndex = _editorTabs.SelectedItem is TabItem selected
            ? _sqlEditors.FindIndex(item => ReferenceEquals(item.Tab, selected))
            : 0;
        var snapshot = new SqlEditorRecoverySnapshot(
            1,
            DateTimeOffset.UtcNow,
            _sqlEditors.Select(item => item.Tab.Header?.ToString() ?? "SQL Editor").ToArray(),
            _sqlEditors.Select(item => item.Editor.Text ?? string.Empty).ToArray(),
            Math.Max(0, activeIndex));
        await _sqlEditorRecoveryStore.SaveAsync(snapshot, cancellationToken);
        if (!string.IsNullOrWhiteSpace(confirmation))
            ShowFooterNotification(confirmation, "#397B45");
    }

    private async Task RestoreSqlEditorRecoveryAsync()
    {
        var snapshot = await _sqlEditorRecoveryStore.LoadAsync();
        if (snapshot is null)
            return;

        if (snapshot.Version != 1 || snapshot.Queries.Length == 0 ||
            snapshot.Queries.Length > 50 || snapshot.Titles.Length != snapshot.Queries.Length)
        {
            ReportError("SQL editor recovery", "The recovery file has an unsupported version or invalid editor data. It was not restored.");
            return;
        }

        _isRestoringEditors = true;
        try
        {
            for (var index = 0; index < snapshot.Queries.Length; index++)
            {
                (TabItem Tab, AvaloniaEdit.TextEditor Editor) editorTab;
                if (index == 0)
                {
                    editorTab = _sqlEditors[0];
                }
                else
                {
                    CreateSqlEditorTab();
                    editorTab = _sqlEditors[^1];
                }

                editorTab.Tab.Header = string.IsNullOrWhiteSpace(snapshot.Titles[index])
                    ? $"SQL Editor {index + 1}"
                    : snapshot.Titles[index];
                editorTab.Editor.Text = snapshot.Queries[index] ?? string.Empty;
            }

            var activeIndex = Math.Clamp(snapshot.ActiveEditorIndex, 0, _sqlEditors.Count - 1);
            _editorTabs.SelectedItem = _sqlEditors[activeIndex].Tab;
        }
        finally
        {
            _isRestoringEditors = false;
        }

        ReportInformation(
            "Saved SQL workspace",
            $"Restored {snapshot.Queries.Length} SQL editor tab(s) from the previous session (last saved {snapshot.SavedAt.ToLocalTime():g}). Review the restored queries before executing them.");
    }

    private void ReportError(string context, Exception exception, string? secretToRedact = null)
    {
        var details = exception.ToString();
        if (!string.IsNullOrEmpty(secretToRedact))
            details = details.Replace(secretToRedact, "[REDACTED]", StringComparison.Ordinal);
        ReportError(context, details);
    }

    private void ReportError(string context, string details)
    {
        AddMessage("Error", context, details);
        _statusText.Text = $"{context} failed. See the red diagnostic entry below.";
        _resultsMessagesTabs.SelectedItem = _messagesTab;
        _messagesScrollViewer.Offset = new Avalonia.Vector(0, 0);
        ShowFooterNotification($"Error: {context}", "#B83B35");
    }

    private void ReportInformation(string context, string details)
    {
        AddMessage("Information", context, details);
        _statusText.Text = details;
        _resultsMessagesTabs.SelectedItem = _messagesTab;
        _messagesScrollViewer.Offset = new Avalonia.Vector(0, 0);
        ShowFooterNotification(details, "#9A7100");
    }

    private void AddMessage(string severity, string context, string details)
    {
        const int maximumDetailsCharacters = 30_000;
        if (details.Length > maximumDetailsCharacters)
            details = details[..maximumDetailsCharacters] + Environment.NewLine + "[Message details truncated.]";
        _messages.Insert(0, new StudioMessageEntry(DateTimeOffset.Now, severity, context, details));
        while (_messages.Count > 500)
            _messages.RemoveAt(_messages.Count - 1);
    }

    private void ShowFooterNotification(string message, string color)
    {
        _footerNotificationCancellation?.Cancel();
        var cancellation = new CancellationTokenSource();
        _footerNotificationCancellation = cancellation;
        _footerNotificationText.Text = message;
        _footerNotificationText.Foreground = new SolidColorBrush(Color.Parse(color));
        _footerNotificationText.IsVisible = true;
        _ = HideFooterNotificationAfterDelayAsync(cancellation);
    }

    private async Task HideFooterNotificationAfterDelayAsync(CancellationTokenSource cancellation)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(3), cancellation.Token);
            if (ReferenceEquals(_footerNotificationCancellation, cancellation))
                _footerNotificationText.IsVisible = false;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        finally
        {
            if (ReferenceEquals(_footerNotificationCancellation, cancellation))
                _footerNotificationCancellation = null;
            cancellation.Dispose();
        }
    }

    private void UpdateConnectionState()
    {
        var sessions = _sessionManager.GetActiveSessions();
        _connectionStateText.Text = sessions.Count switch
        {
            0 => "Not connected",
            1 => $"Connected: {sessions[0].ProfileName} · {sessions[0].ServerVersion}",
            _ => $"{sessions.Count} active database sessions"
        };
        _connectionStateText.Foreground = new SolidColorBrush(
            Color.Parse(sessions.Count == 0 ? "#D96C6C" : "#55A978"));
        _connectionStateText.FontWeight = FontWeight.Bold;
        UpdateActionAvailability();
    }

    private async Task PrepareForUpdateShutdownAsync()
    {
        _updateCheckLifetime.Cancel();
        _queryExecutionCancellation?.Cancel();
        _metadataSearchCancellation?.Cancel();
        _footerNotificationCancellation?.Cancel();

        try
        {
            await SaveSqlEditorRecoveryNowAsync(confirmation: null);
        }
        catch (Exception exception)
        {
            ReportError("Saving SQL editor workspace before update", exception);
        }

        _recoverySaveCancellation?.Cancel();

        try
        {
            await ((IAsyncDisposable)_sessionManager).DisposeAsync();
        }
        catch (Exception exception)
        {
            ReportError("Closing database sessions before update", exception);
        }

        _isPreparedForUpdateShutdown = true;
    }

    private async void OnWindowClosed(object? sender, EventArgs e)
    {
        if (_isPreparedForUpdateShutdown)
            return;

        _updateCheckLifetime.Cancel();
        _queryExecutionCancellation?.Cancel();
        _metadataSearchCancellation?.Cancel();
        _footerNotificationCancellation?.Cancel();
        try
        {
            // Persist the latest workspace before closing. Unlike a temporary crash marker,
            // this file is intentionally retained so editor tabs survive normal restarts.
            await SaveSqlEditorRecoveryNowAsync(confirmation: null);
        }
        catch (Exception exception)
        {
            ReportError("Saving SQL editor workspace before closing", exception);
        }

        _recoverySaveCancellation?.Cancel();

        try
        {
            await ((IAsyncDisposable)_sessionManager).DisposeAsync();
        }
        catch (Exception exception)
        {
            ReportError("Closing database sessions", exception);
        }
    }

    private sealed record QueryResultRow(string[] Values);
    private sealed record TableFieldDisplay(string Name, string? Domain, string Type, string Nullable, string Position);


    private sealed class SqlCompletionData(SqlCompletionSuggestion suggestion) : ICompletionData
    {
        public IImage? Image => null;
        public string Text => suggestion.Text;
        public object Content => suggestion.Text;
        public object Description => suggestion.Description;
        public double Priority => 0;

        public void Complete(TextArea textArea, ISegment completionSegment, EventArgs insertionRequestEventArgs)
        {
            var caretOffset = textArea.Caret.Offset;
            var prefix = SqlCompletionCatalog.GetPrefix(textArea.Document.Text, caretOffset);
            var replacementOffset = caretOffset - prefix.Length;
            textArea.Document.Replace(replacementOffset, prefix.Length, suggestion.InsertText);
        }
    }


}
