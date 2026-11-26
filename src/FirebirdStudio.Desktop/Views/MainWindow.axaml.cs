using System.Text.RegularExpressions;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Styling;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Avalonia.Threading;
using Avalonia.Input;
using Avalonia.Input.Platform;
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
using static FirebirdStudio.Desktop.Services.StudioText;

namespace FirebirdStudio.Desktop.Views;

public sealed partial class MainWindow : Window
{
    private readonly ConnectionProfileManager _profileManager;
    private readonly IFirebirdSessionManager _sessionManager;
    private readonly ISecureCredentialStore _credentialStore;
    private readonly StudioPreferencesStore _preferencesStore;
    private readonly Task<StudioPreferences>? _startupPreferences;
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
    private readonly ObservableCollection<ConnectionListItem> _connectionItems = [];
    private readonly HashSet<Guid> _pendingConnectionChanges = [];
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
    private readonly Button _editSelectedConnectionButton;
    private readonly Button _connectionSessionButton;
    private readonly TreeView _metadataTree;
    private readonly TextBox _metadataSearchBox;
    private readonly ComboBox _metadataSearchKindFilter;
    private readonly Button _clearMetadataSearchButton;
    private readonly TextBlock _metadataConnectionText;
    private readonly TextBlock _metadataEmptyText;
    private readonly Button _refreshMetadataButton;
    private readonly MenuItem _refreshMetadataMenuItem;
    private readonly MenuItem _organizeMetadataMenuItem;
    private readonly MenuItem _moveCategoryUpMenuItem;
    private readonly MenuItem _moveCategoryDownMenuItem;
    private readonly MenuItem _executeButton;
    private readonly Button _executeQueryAction;
    private readonly MenuItem _sqlAutocompleteMenuItem;
    private readonly MenuItem _cancelQueryButton;
    private readonly QueryHistoryView _dockedQueryHistory;
    private readonly QueryHistoryView _tabbedQueryHistory;
    private readonly QueryResultsView _queryResults;
    private readonly TabControl _editorTabs;
    private sealed record SqlEditorState(TabItem Tab, AvaloniaEdit.TextEditor Editor);

    private readonly List<SqlEditorState> _sqlEditors = [];
    private TabItem? _lastActiveSqlTab;
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
    private readonly Button _expandHistoryDockButton;
    private GridLength _expandedHistoryDockWidth = new(300);
    private bool _isHistoryDockCollapsed;
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
    private readonly MessagesView _messagesView;
    private readonly TabControl _resultsMessagesTabs;
    private readonly TabItem _resultsTab;
    private readonly TabItem _messagesTab;
    private readonly TextBlock _footerNotificationText;
    private readonly TextBlock _connectionStateText;
    private readonly Version _currentVersion;
    private string? _currentTableDetailsName;

    private ConnectionProfile? SelectedConnection => (_connectionsList.SelectedItem as ConnectionListItem)?.Profile;

    public MainWindow() : this(new StudioPreferencesStore(), null)
    {
    }

    internal MainWindow(StudioPreferencesStore preferencesStore, Task<StudioPreferences>? startupPreferences)
    {
        _preferencesStore = preferencesStore;
        _startupPreferences = startupPreferences;
        AvaloniaXamlLoader.Load(this);

        var sqlEditor = this.FindControl<AvaloniaEdit.TextEditor>("SqlEditor")!;
        var sqlEditorTab = this.FindControl<TabItem>("SqlEditorTab")!;
        ConfigureSqlEditorTab(sqlEditorTab, sqlEditor);
        _lastActiveSqlTab = sqlEditorTab;
        ApplySqlSyntaxHighlighting(sqlEditor);

        var repository = new JsonConnectionProfileRepository();
        _profileManager = new ConnectionProfileManager(repository);
        _sessionManager = new FirebirdSessionManager();
        _credentialStore = new LatchkeySecureCredentialStore();

        _connectionsList = this.FindControl<ListBox>("ConnectionsList")!;
        _editSelectedConnectionButton = this.FindControl<Button>("EditSelectedConnectionButton")!;
        _connectionSessionButton = this.FindControl<Button>("ConnectionSessionButton")!;
        this.FindControl<Button>("NewConnectionButton")!.Click += OnAddConnectionClick;
        _editSelectedConnectionButton.Click += OnEditConnectionClick;
        _connectionSessionButton.Click += OnConnectionSessionClick;
        var createConnectionContextItem = new MenuItem { Header = T("CreateConnectionDialog") };
        _connectionContextEditItem = new MenuItem { Header = T("EditConnectionDialog"), IsEnabled = false };
        _connectionContextConnectItem = new MenuItem { Header = T("ConnectToDatabase"), IsEnabled = false };
        _connectionContextDisconnectItem = new MenuItem { Header = T("DisconnectFromDatabase"), IsEnabled = false };
        _connectionContextDeleteItem = new MenuItem { Header = T("DeleteConnection"), IsEnabled = false };
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
        _metadataSearchKindFilter = this.FindControl<ComboBox>("MetadataSearchKindFilter")!;
        _clearMetadataSearchButton = this.FindControl<Button>("ClearMetadataSearchButton")!;
        _metadataConnectionText = this.FindControl<TextBlock>("MetadataConnectionText")!;
        _metadataEmptyText = this.FindControl<TextBlock>("MetadataEmptyText")!;
        _refreshMetadataButton = this.FindControl<Button>("RefreshMetadataButton")!;
        _refreshMetadataMenuItem = this.FindControl<MenuItem>("RefreshMetadataMenuItem")!;
        _organizeMetadataMenuItem = this.FindControl<MenuItem>("OrganizeMetadataMenuItem")!;
        _moveCategoryUpMenuItem = this.FindControl<MenuItem>("MoveCategoryUpMenuItem")!;
        _moveCategoryDownMenuItem = this.FindControl<MenuItem>("MoveCategoryDownMenuItem")!;
        _executeButton = this.FindControl<MenuItem>("ExecuteMenuItem")!;
        _executeQueryAction = this.FindControl<Button>("ExecuteQueryButton")!;
        _sqlAutocompleteMenuItem = this.FindControl<MenuItem>("SqlAutocompleteMenuItem")!;
        _sqlAutocompleteMenuItem.Click += async (_, _) => await ShowSqlCompletionAsync();
        this.FindControl<MenuItem>("FormatSqlMenuItem")!.Click += (_, _) => FormatActiveSql();
        _cancelQueryButton = this.FindControl<MenuItem>("CancelQueryMenuItem")!;
        _dockedQueryHistory = this.FindControl<QueryHistoryView>("DockedQueryHistory")!;
        _tabbedQueryHistory = this.FindControl<QueryHistoryView>("TabbedQueryHistory")!;
        _queryResults = this.FindControl<QueryResultsView>("QueryResults")!;
        _editorTabs = this.FindControl<TabControl>("EditorTabs")!;
        _addSqlEditorTab = this.FindControl<TabItem>("AddSqlEditorTab")!;
        _editorTabs.SelectionChanged += (_, e) =>
        {
            if (ReferenceEquals(e.Source, _editorTabs) && _editorTabs.SelectedItem is TabItem selected &&
                _sqlEditors.Any(item => ReferenceEquals(item.Tab, selected)))
                _lastActiveSqlTab = selected;

            if (!_isRestoringEditors && !ReferenceEquals(_editorTabs.SelectedItem, _addSqlEditorTab))
                ScheduleSqlEditorRecoverySave(showNotification: false);
        };
        if (_addSqlEditorTab.Header is Grid addEditorHeader &&
            addEditorHeader.Children.OfType<Button>().FirstOrDefault() is { } addEditorButton)
            addEditorButton.Click += (_, _) => CreateSqlEditorTab();
        _tableDetailsTab = this.FindControl<TabItem>("TableDetailsTab")!;
        var closeTableDetailsItem = new MenuItem { Header = T("CloseTab") };
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
        ApplySqlSyntaxHighlighting(_tableDdlText);
        _readOnlySqlEditors.Add(_tableDdlText);
        _workspaceGrid = this.FindControl<Grid>("WorkspaceGrid")!;
        _historySplitter = this.FindControl<GridSplitter>("HistorySplitter")!;
        _queryHistoryPanel = this.FindControl<Border>("QueryHistoryPanel")!;
        _expandHistoryDockButton = this.FindControl<Button>("ExpandHistoryDockButton")!;
        _queryHistoryTab = this.FindControl<TabItem>("QueryHistoryTab")!;
        _historyDockRightMenuItem = this.FindControl<MenuItem>("HistoryDockRightMenuItem")!;
        _historyDockTabMenuItem = this.FindControl<MenuItem>("HistoryDockTabMenuItem")!;
        _toggleThemeMenuItem = this.FindControl<MenuItem>("ToggleThemeMenuItem")!;
        _checkForUpdatesMenuItem = this.FindControl<MenuItem>("CheckForUpdatesMenuItem")!;
        _editButton = this.FindControl<MenuItem>("EditConnectionMenuItem")!;
        _removeButton = this.FindControl<MenuItem>("RemoveConnectionMenuItem")!;
        _connectButton = this.FindControl<MenuItem>("ConnectMenuItem")!;
        _disconnectButton = this.FindControl<MenuItem>("DisconnectMenuItem")!;
        _messagesView = this.FindControl<MessagesView>("Messages")!;
        _statusText = _messagesView.CurrentStatus;
        this.FindControl<TextBlock>("WorkspaceStatusText")!
            .Bind(TextBlock.TextProperty, _statusText.GetObservable(TextBlock.TextProperty));
        _messagesView.BindEntries(_messages);
        _resultsMessagesTabs = this.FindControl<TabControl>("ResultsMessagesTabs")!;
        _resultsTab = this.FindControl<TabItem>("ResultsTab")!;
        _messagesTab = this.FindControl<TabItem>("MessagesTab")!;
        _messagesView.ClearRequested += (_, _) => ClearMessages();
        _messagesView.CopyDetailsRequested += async details =>
            await CopyToClipboardAsync(details, T("CopyingMessageDetails"), T("MessageDetailsCopied"));
        _queryResults.ClearRequested += (_, _) => ClearResults();
        _queryResults.CopyRowRequested += async row =>
            await CopyToClipboardAsync(row, T("CopyingResultRow"), T("ResultRowCopied"));
        var toggleConnectionDisplayButton = this.FindControl<Button>("ToggleConnectionDisplayButton")!;
        toggleConnectionDisplayButton.Click += async (_, _) =>
            await SetConnectionDisplayModeAsync(_preferences.ConnectionDisplayMode == "Detailed" ? "Minimal" : "Detailed");
        _footerNotificationText = this.FindControl<TextBlock>("FooterNotificationText")!;
        _connectionStateText = this.FindControl<TextBlock>("ConnectionStateText")!;
        _currentVersion = GitHubReleaseChecker.NormalizeVersion(
            typeof(MainWindow).Assembly.GetName().Version ?? new Version(0, 1, 0));
        Title = $"Firebird Studio - {_currentVersion}";

        _connectionsList.ItemsSource = _connectionItems;
        var detailedConnectionsMenuItem = this.FindControl<MenuItem>("ConnectionDisplayDetailedMenuItem")!;
        var minimalConnectionsMenuItem = this.FindControl<MenuItem>("ConnectionDisplayMinimalMenuItem")!;
        detailedConnectionsMenuItem.Click += async (_, _) => await SetConnectionDisplayModeAsync("Detailed");
        minimalConnectionsMenuItem.Click += async (_, _) => await SetConnectionDisplayModeAsync("Minimal");
        _metadataTree.ItemsSource = _metadataObjects;
        foreach (var history in new[] { _dockedQueryHistory, _tabbedQueryHistory })
        {
            history.BindEntries(_queryHistory);
            history.RestoreRequested += RestoreQueryFromHistory;
            history.ClearRequested += async (_, _) => await ClearQueryHistoryAsync();
        }
        _dockedQueryHistory.ShowDockControls();
        _dockedQueryHistory.CollapseRequested += (_, _) => SetHistoryDockCollapsed(true);
        _expandHistoryDockButton.Click += (_, _) => SetHistoryDockCollapsed(false);
        _refreshMetadataButton.Click += OnRefreshMetadataClick;
        _previousTableDataPageButton.Click += async (_, _) => await ChangeTableDataPageAsync(-1);
        _nextTableDataPageButton.Click += async (_, _) => await ChangeTableDataPageAsync(1);
        _moveCategoryUpMenuItem.Click += OnMoveCategoryUpClick;
        _moveCategoryDownMenuItem.Click += OnMoveCategoryDownClick;
        _executeButton.Click += OnExecuteClick;
        _cancelQueryButton.Click += OnCancelQueryClick;
        _executeQueryAction.Click += OnExecuteClick;
        this.FindControl<Button>("CancelQueryButton")!.Click += OnCancelQueryClick;
        this.FindControl<Button>("FormatQueryButton")!.Click += (_, _) => FormatActiveSql();
        _connectionsList.SelectionChanged += OnSelectionChanged;
        _connectionsList.DoubleTapped += OnConnectionsDoubleTapped;
        _connectionsList.AddHandler(InputElement.KeyDownEvent, OnConnectionsKeyDown, RoutingStrategies.Tunnel);
        _connectionsList.PointerPressed += OnConnectionsPointerPressed;
        createConnectionContextItem.Click += OnAddConnectionClick;
        _connectionContextEditItem.Click += OnEditConnectionClick;
        _connectionContextConnectItem.Click += OnConnectClick;
        _connectionContextDisconnectItem.Click += OnDisconnectClick;
        _connectionContextDeleteItem.Click += OnRemoveConnectionClick;
        _metadataSearchBox.TextChanged += OnMetadataSearchChanged;
        _metadataSearchKindFilter.SelectionChanged += OnMetadataSearchChanged;
        _clearMetadataSearchButton.Click += (_, _) =>
        {
            _metadataSearchBox.Focus();
            _metadataSearchBox.Clear();
        };
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
        this.FindControl<MenuItem>("SettingsMenuItem")!.Click += OnSettingsClick;
        this.FindControl<MenuItem>("CreateDatabaseMenuItem")!.Click += OnCreateDatabaseClick;
        _toggleThemeMenuItem.Click += async (_, _) =>
            await SetThemeAsync(_preferences.Theme == "Dark" ? "Light" : "Dark");
        _checkForUpdatesMenuItem.Click += async (_, _) =>
            await CheckForUpdatesAsync(manual: true, cancellationToken: _updateCheckLifetime.Token);
        this.FindControl<MenuItem>("AboutMenuItem")!.Click += async (_, _) =>
            await new AboutWindow(_currentVersion).ShowDialog(this);
        _historyDockRightMenuItem.Click += (_, _) => SetHistoryDockMode(true);
        _historyDockTabMenuItem.Click += (_, _) => SetHistoryDockMode(false);
        ApplyExecuteSqlShortcut();
        KeyDown += OnWindowKeyDown;
        Opened += OnWindowOpened;
        Closed += OnWindowClosed;
    }

    private async void OnWindowOpened(object? sender, EventArgs e)
    {
        try
        {
            _preferences = await (_startupPreferences ?? _preferencesStore.LoadAsync());
            if (_preferences.Theme == "Default")
                _preferences = _preferences with { Theme = "Light" };
            ApplyTheme();
            ApplyConnectionDisplayMode();
            ApplyExecuteSqlShortcut();
            SetHistoryDockMode(true);
        }
        catch (Exception exception)
        {
            ReportError(T("SettingsLoadError"), exception);
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
            ReportError(T("LoadingHistory"), exception);
        }

        try
        {
            var profiles = await _profileManager.GetAllAsync();
            _connectionItems.Clear();
            foreach (var profile in profiles.OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
                _connectionItems.Add(new ConnectionListItem(profile));

            _statusText.Text = T("SavedProfilesLoaded", _connectionItems.Count);
            UpdateConnectionState();
        }
        catch (Exception exception)
        {
            ReportError(T("LoadingProfiles"), exception);
        }

        try
        {
            await RestoreSqlEditorRecoveryAsync();
        }
        catch (Exception exception)
        {
            ReportError(T("SqlEditorRecovery"), exception);
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
                ShowFooterNotification(T("UpdateCheckRunning"), "information");
            return;
        }

        try
        {
            if (manual)
                ShowFooterNotification(T("CheckingUpdatesStatus"), "information");

            var latestRelease = await _releaseChecker.GetLatestStableReleaseAsync(cancellationToken);
            if (latestRelease is null)
            {
                if (manual)
                    ShowFooterNotification(T("NoStableRelease"), "information");
                return;
            }

            var assemblyVersion = typeof(MainWindow).Assembly.GetName().Version ?? new Version(0, 0, 0);
            var currentVersion = GitHubReleaseChecker.NormalizeVersion(assemblyVersion);
            if (latestRelease.Version <= currentVersion)
            {
                if (manual)
                    ShowFooterNotification(T("AppUpToDate"), "success");
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
                ReportError(T("CheckingUpdates"), exception);
            else
                AddMessage("Warning", T("AutomaticUpdateCheck"), exception.Message);
        }
        finally
        {
            _updateCheckGate.Release();
        }
    }

    private async Task ShowUpdateAvailableDialogAsync(AvailableRelease release, Version currentVersion)
    {
        var notes = string.IsNullOrWhiteSpace(release.ReleaseNotes)
            ? T("NoReleaseNotes")
            : release.ReleaseNotes.Trim();
        if (notes.Length > 8_000)
            notes = notes[..8_000] + Environment.NewLine + T("ReleaseNotesTruncated");

        var dialog = new Window
        {
            Title = T("UpdateAvailable"),
            Width = 560,
            SizeToContent = SizeToContent.Height,
            MaxHeight = 620,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false
        };

        var viewReleaseButton = CreateDialogButton(T("ViewRelease"), () =>
        {
            dialog.Close();
            try
            {
                Process.Start(new ProcessStartInfo(release.HtmlUrl.AbsoluteUri) { UseShellExecute = true });
            }
            catch (Exception exception)
            {
                ReportError(T("OpeningReleasePage"), exception);
            }
        });
        var laterButton = CreateDialogButton(T("Later"), () => dialog.Close());
        var downloadButton = CreateDialogButton(T("DownloadUpdate"), () =>
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
                    Text = T("NewVersionAvailable", release.TagName, currentVersion),
                    FontSize = 14,
                    FontWeight = FontWeight.SemiBold,
                    TextWrapping = TextWrapping.Wrap
                },
                new TextBlock { Text = T("ReleaseNotes"), FontWeight = FontWeight.SemiBold },
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
            Text = T("PreparingDownload"),
            TextWrapping = TextWrapping.Wrap
        };
        string? downloadDirectory = null;
        var openFolderButton = CreateDialogButton(T("OpenDownloadFolder"), () =>
        {
            if (downloadDirectory is not { } directory || !Directory.Exists(directory))
                return;

            try
            {
                Process.Start(new ProcessStartInfo(directory) { UseShellExecute = true });
            }
            catch (Exception exception)
            {
                ReportError(T("OpeningDownloadFolder"), exception);
            }
        });
        openFolderButton.IsVisible = false;

        var dialog = new Window
        {
            Title = T("DownloadAppVersion", release.Version),
            Width = 520,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false
        };

        var installButton = CreateDialogButton(T("InstallRestart"), () =>
        {
            installRequested = true;
            dialog.Close();
        });
        installButton.IsVisible = false;

        var cancelButton = new Button { Content = T("CancelDownload") };
        var downloadFinished = false;
        cancelButton.Click += (_, _) =>
        {
            if (downloadFinished)
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
                    Text = T("DownloadingAppVersion", release.Version),
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
                            ? T("DownloadProgress", value.Percentage, FormatBytes(value.BytesReceived), FormatBytes(value.TotalBytes))
                            : T("DownloadBytes", FormatBytes(value.BytesReceived));
                    }));

                downloadedUpdate = await _updateDownloader.DownloadAsync(
                    release, progress, cancellation.Token);

                progressBar.Value = 100;
                downloadDirectory = downloadedUpdate.DirectoryPath;
                openFolderButton.IsVisible = true;

                var updaterExecutable = GetUpdaterExecutablePath();
                if (File.Exists(updaterExecutable))
                {
                    statusText.Text = T("VerifiedDownloadReady", downloadedUpdate.FileName);
                    installButton.IsVisible = true;
                }
                else
                {
                    var reason = T("DevelopmentUpdateNotice");
                    statusText.Text = T("VerifiedDownloadManual", reason, downloadedUpdate.FileName);
                }
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
                if (dialog.IsVisible)
                {
                    statusText.Text = T("DownloadCancelled");
                }
            }
            catch (Exception exception)
            {
                if (dialog.IsVisible)
                {
                    statusText.Text = T("DownloadFailed", exception.Message);
                    ReportError(T("DownloadingUpdate"), exception);
                }
            }
            finally
            {
                downloadFinished = true;
                cancelButton.Content = T("Close");
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
                ReportError(T("StartingUpdater"), exception);
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
            throw new InvalidOperationException(T("WaitToInstallUpdate"));

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

    private async void OnCreateDatabaseClick(object? sender, RoutedEventArgs e)
    {
        var source = SelectedConnection;
        var session = _sessionManager.GetActiveSessions().FirstOrDefault(item => item.ProfileId == source?.Id);
        if (session is null && !EmbeddedFirebirdInstaller.IsSupported)
            return;

        string? savedPassword = null;
        if (session is not null && source is not null && source.EmbeddedEngineVersion is null)
        {
            try
            {
                savedPassword = await _credentialStore.GetPasswordAsync(source.Id);
            }
            catch (Exception exception)
            {
                ReportError(T("AccessingCredentialStore"), exception);
            }
        }

        var result = await new CreateDatabaseWindow(session is null ? null : source, session?.ServerVersion, savedPassword,
                ((App)Avalonia.Application.Current!).EmbeddedEngines)
            .ShowDialog<CreatedDatabase?>(this);
        if (result is null)
            return;

        if (result.NewProfile is { } profile)
        {
            try
            {
                await _profileManager.SaveAsync(profile);
                _connectionItems.Add(new ConnectionListItem(profile));
            }
            catch (Exception exception)
            {
                ReportError(T("CreatedDatabaseProfileFailed", result.Path), exception);
                return;
            }
        }

        var message = T("DatabaseCreated", result.Path);
        _statusText.Text = message;
        AddMessage("Success", T("CreateDatabase"), message);
        ShowFooterNotification(T("CreateDatabaseSucceeded"), "success");
    }

    private async void OnEditConnectionClick(object? sender, RoutedEventArgs e)
    {
        if (SelectedConnection is { } profile)
            await ShowConnectionEditorAsync(profile);
    }

    private async Task ShowConnectionEditorAsync(ConnectionProfile profile)
    {
        var editor = new ConnectionEditorWindow(profile, _profileManager, new FirebirdConnectionTester());
        await editor.ShowDialog(this);

        if (editor.SavedProfile is not { } savedProfile)
            return;

        var row = _connectionItems.FirstOrDefault(item => item.Profile.Id == savedProfile.Id);
        if (row is null)
        {
            row = new ConnectionListItem(savedProfile);
            _connectionItems.Add(row);
        }
        else
        {
            row.UpdateProfile(savedProfile);
        }

        _connectionsList.SelectedItem = row;
        UpdateConnectionState();
        _statusText.Text = T("ProfileSaved", savedProfile.Name);
    }

    private async void OnRemoveConnectionClick(object? sender, RoutedEventArgs e)
    {
        if (_connectionsList.SelectedItem is not ConnectionListItem row)
            return;

        var profile = row.Profile;
        if (_sessionManager.GetActiveSessions().Any(session => session.ProfileId == profile.Id))
        {
            _statusText.Text = T("DisconnectBeforeRemoving");
            return;
        }

        if (!await ConfirmDeleteConnectionAsync(profile))
            return;

        try
        {
            // Remove the profile first. If persistence fails, keep its saved credential intact.
            await _profileManager.DeleteAsync(profile.Id);
            _connectionItems.Remove(row);

            try
            {
                await _credentialStore.DeletePasswordAsync(profile.Id);
                _statusText.Text = T("ProfileRemoved", profile.Name);
            }
            catch (Exception credentialException)
            {
                ReportError(T("RemovingCredential", profile.Name), credentialException);
            }
        }
        catch (Exception exception)
        {
            ReportError(T("RemovingProfile", profile.Name), exception);
        }
    }

    private async Task<bool> ConfirmDeleteConnectionAsync(ConnectionProfile profile)
    {
        var confirmation = new Window
        {
            Title = T("DeleteConnection"),
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
                    Text = T("ConfirmDeleteConnection", profile.Name),
                    TextWrapping = TextWrapping.Wrap
                },
                new StackPanel
                {
                    Orientation = Avalonia.Layout.Orientation.Horizontal,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                    Spacing = 8,
                    Children =
                    {
                        CreateDialogButton(T("Cancel"), () => CloseConfirmation(false)),
                        CreateDialogButton(T("Delete"), () => CloseConfirmation(true))
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
        if (SelectedConnection is { } profile)
            await ConnectProfileAsync(profile);
    }

    private void OnConnectionsPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(_connectionsList).Properties.IsRightButtonPressed)
            return;

        if (e.Source is Visual source)
        {
            var item = source as ListBoxItem ?? source.GetVisualAncestors().OfType<ListBoxItem>().FirstOrDefault();
            if (item?.DataContext is ConnectionListItem row)
                _connectionsList.SelectedItem = row;
        }

        UpdateActionAvailability();
    }

    private async void OnConnectionsDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (SelectedConnection is { } profile)
            await ConnectProfileAsync(profile);
    }

    private async void OnConnectionsKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || e.KeyModifiers != KeyModifiers.None || SelectedConnection is not { } profile)
            return;

        e.Handled = true;
        await ConnectProfileAsync(profile);
    }

    private async void OnConnectionSessionClick(object? sender, RoutedEventArgs e)
    {
        if (SelectedConnection is not { } profile)
            return;

        if (_sessionManager.GetActiveSessions().Any(session => session.ProfileId == profile.Id))
            await DisconnectProfileAsync(profile);
        else
            await ConnectProfileAsync(profile);
    }

    private async Task ConnectProfileAsync(ConnectionProfile profile)
    {
        if (_sessionManager.GetActiveSessions().Any(session => session.ProfileId == profile.Id))
        {
            _statusText.Text = T("AlreadyConnected", profile.Name);
            return;
        }

        if (!_pendingConnectionChanges.Add(profile.Id))
            return;

        UpdateActionAvailability();
        try
        {
            await OpenConnectionAsync(profile);
        }
        finally
        {
            _pendingConnectionChanges.Remove(profile.Id);
            UpdateConnectionState();
        }
    }

    private async Task OpenConnectionAsync(ConnectionProfile profile)
    {
        string? savedPassword = null;
        string? credentialStoreNotice = null;
        try
        {
            savedPassword = await _credentialStore.GetPasswordAsync(profile.Id);
        }
        catch (Exception exception)
        {
            credentialStoreNotice = T("CredentialStoreUnavailable", exception.Message);
            ReportError(T("AccessingCredentialStore"), exception);
        }

        var passwordPrompt = new ConnectionPasswordWindow(profile.Name, savedPassword, credentialStoreNotice);
        await passwordPrompt.ShowDialog(this);
        if (passwordPrompt.Password is not { } password)
        {
            savedPassword = null;
            return;
        }

        _statusText.Text = T("ConnectingToProfile", profile.Name);
        try
        {
            var resolved = await ((App)Avalonia.Application.Current!).EmbeddedEngines.ResolveAsync(profile,
                new Progress<string>(version => _statusText.Text = T("DownloadingFirebirdEngine", version)));
            var result = await _sessionManager.ConnectAsync(resolved, password);
            if (result.Succeeded)
            {
                _statusText.Text = result.Message;
                AddMessage("Success", T("ConnectionToProfile", profile.Name), result.Message);
                ShowFooterNotification(T("ConnectionEstablished"), "success");
            }
            else
                ReportError(T("ConnectionToProfile", profile.Name), result.Diagnostics ?? result.Message);
            UpdateConnectionState();

            if (result.Succeeded)
            {
                if (SelectedConnection?.Id == profile.Id)
                {
                    _statusText.Text = T("ConnectedLoadingObjects");
                    AddMessage("Information", T("DatabaseMetadata"), T("LoadingFirstMetadata"));
                    await RefreshMetadataAsync(profile);
                }

                try
                {
                    if (passwordPrompt.RememberPassword && password.Length > 0)
                    {
                        await _credentialStore.SavePasswordAsync(profile.Id, password);
                        _statusText.Text = T("PasswordSaved");
                    }
                    else if (passwordPrompt.RememberPassword)
                    {
                        await _credentialStore.DeletePasswordAsync(profile.Id);
                        _statusText.Text = T("EmptyPasswordNotSaved");
                    }
                    else if (savedPassword is not null)
                    {
                        await _credentialStore.DeletePasswordAsync(profile.Id);
                        _statusText.Text = T("PasswordRemoved");
                    }
                }
                catch (Exception exception)
                {
                    ReportError(T("UpdatingCredential"), exception);
                }
            }
        }
        catch (OperationCanceledException)
        {
            _statusText.Text = T("ConnectCancelled");
        }
        catch (Exception exception)
        {
            ReportError(T("ConnectionToProfile", profile.Name), exception, password);
        }
        finally
        {
            password = string.Empty;
            savedPassword = null;
            credentialStoreNotice = null;
            passwordPrompt.ClearCapturedPassword();
        }
    }

    private async void OnDisconnectClick(object? sender, RoutedEventArgs e)
    {
        if (SelectedConnection is { } profile)
            await DisconnectProfileAsync(profile);
    }

    private async Task DisconnectProfileAsync(ConnectionProfile profile)
    {
        if (!_pendingConnectionChanges.Add(profile.Id))
            return;

        if (SelectedConnection?.Id == profile.Id)
        {
            CancelMetadataLoading();
            _metadataSearchCancellation?.Cancel();
        }

        UpdateActionAvailability();
        try
        {
            await _sessionManager.DisconnectAsync(profile.Id);
            if (SelectedConnection?.Id == profile.Id)
            {
                CancelMetadataLoading();
                ResetMetadataSearch();
                _metadataObjects.Clear();
                _currentTableDetailsName = null;
                _tableDetailsTab.IsVisible = false;
            }
            _statusText.Text = T("DisconnectedFromProfile", profile.Name);
            ReportInformation(T("DatabaseConnection"), _statusText.Text);
        }
        catch (Exception exception)
        {
            ReportError(T("DisconnectingFromProfile", profile.Name), exception);
        }
        finally
        {
            _pendingConnectionChanges.Remove(profile.Id);
            UpdateConnectionState();
        }
    }

    private async void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        CancelMetadataLoading();
        ResetMetadataSearch();
        _metadataObjects.Clear();
        _completionWindow?.Close();
        _currentTableDetailsName = null;
        _currentTableDataPage = null;
        _tableDetailsTab.IsVisible = false;
        _previousTableDataPageButton.IsEnabled = false;
        _nextTableDataPageButton.IsEnabled = false;
        _tableDataPageStatus.Text = T("NoRowsLoaded");
        UpdateActionAvailability();

        if (_connectionsList.SelectedItem is ConnectionListItem { IsConnected: true } row)
            await RefreshMetadataAsync(row.Profile);
    }

    private void UpdateActionAvailability()
    {
        var selected = SelectedConnection;
        var sessions = _sessionManager.GetActiveSessions();
        var selectedSession = sessions.FirstOrDefault(session => session.ProfileId == selected?.Id);
        var isConnected = selectedSession is not null;
        var canChange = selected is not null && !_pendingConnectionChanges.Contains(selected.Id);

        _editButton.IsEnabled = canChange;
        _removeButton.IsEnabled = canChange && !isConnected;
        _connectButton.IsEnabled = canChange && !isConnected;
        _disconnectButton.IsEnabled = canChange && isConnected;
        this.FindControl<MenuItem>("CreateDatabaseMenuItem")!.IsEnabled =
            EmbeddedFirebirdInstaller.IsSupported || (canChange && isConnected);
        _connectionContextEditItem.IsEnabled = _editButton.IsEnabled;
        _connectionContextDeleteItem.IsEnabled = _removeButton.IsEnabled;
        _connectionContextConnectItem.IsEnabled = _connectButton.IsEnabled;
        _connectionContextDisconnectItem.IsEnabled = _disconnectButton.IsEnabled;
        _editSelectedConnectionButton.IsEnabled = _editButton.IsEnabled;
        _connectionSessionButton.IsEnabled = canChange;
        _connectionSessionButton.Content = isConnected ? T("Disconnect") : T("Connect");
        ToolTip.SetTip(_connectionSessionButton,
            isConnected ? T("DisconnectSelected") : T("ConnectSelected"));

        _refreshMetadataButton.IsEnabled = isConnected && !_isMetadataRefreshRunning;
        _refreshMetadataMenuItem.IsEnabled = _refreshMetadataButton.IsEnabled;
        _metadataSearchBox.IsEnabled = isConnected;
        _metadataSearchKindFilter.IsEnabled = isConnected;
        _clearMetadataSearchButton.IsEnabled = isConnected && !string.IsNullOrEmpty(_metadataSearchBox.Text);
        _metadataConnectionText.Text = isConnected ? selected?.Name : null;
        if (!isConnected)
            _metadataEmptyText.Text = T("ConnectToBrowse");
        _executeButton.IsEnabled = isConnected && !_isQueryExecuting;
        _sqlAutocompleteMenuItem.IsEnabled = isConnected;
        _cancelQueryButton.IsEnabled = _isQueryExecuting;
        UpdateMetadataReorderAvailability();

        _connectionStateText.Text = (selected, selectedSession) switch
        {
            ({ } profile, { } session) => T("ConnectedToServer", profile.Name, session.ServerVersion),
            ({ } profile, null) => T("NotConnectedToProfile", profile.Name),
            _ when sessions.Count > 0 => T("ActiveSessions", sessions.Count),
            _ => T("NotConnected")
        };
        var hasActiveSession = isConnected || selected is null && sessions.Count > 0;
        _connectionStateText.Classes.Set("error", !hasActiveSession);
        _connectionStateText.Classes.Set("success", hasActiveSession);
    }

    private async void OnRefreshMetadataClick(object? sender, RoutedEventArgs e)
    {
        if (SelectedConnection is not { } profile)
            return;

        var searchTerm = _metadataSearchBox.Text?.Trim() ?? string.Empty;
        if (searchTerm.Length >= 2)
            await SearchMetadataAsync(profile, searchTerm, debounce: false);
        else
            await RefreshMetadataAsync(profile);
    }

    private async Task RefreshMetadataAsync(ConnectionProfile profile)
    {
        if (SelectedConnection?.Id != profile.Id)
            return;

        CancelMetadataLoading();
        var metadataCancellation = new CancellationTokenSource();
        _metadataLoadingCancellation = metadataCancellation;
        var loadGeneration = _metadataLoadGeneration;
        _isMetadataRefreshRunning = true;
        UpdateActionAvailability();
        _metadataEmptyText.Text = T("LoadingObjects");
        _statusText.Text = T("PreparingCategories", profile.Name);
        try
        {
            // Keep provider work off the UI thread as some provider operations may perform
            // synchronous work before their asynchronous I/O completes.
            var categories = await Task.Run(
                () => _sessionManager.GetMetadataAsync(profile.Id, metadataCancellation.Token),
                metadataCancellation.Token);
            if (metadataCancellation.IsCancellationRequested || loadGeneration != _metadataLoadGeneration ||
                SelectedConnection is not { } currentProfile || currentProfile.Id != profile.Id)
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
                    Details = T("ObjectsLoadInBatches", MetadataPageSize),
                    Children = new BatchObservableCollection<DatabaseMetadataObject>()
                });
            }

            _completionMetadataCache.Remove(profile.Id);
            _completionDomainCache.Remove(profile.Id);
            UpdateMetadataReorderAvailability();

            var failedCategories = new List<string>();
            for (var index = 0; index < orderedCategories.Length; index++)
            {
                if (SelectedConnection is not { } selectedProfile || selectedProfile.Id != profile.Id)
                    return;
                var categoryName = orderedCategories[index].Name;
                _statusText.Text = T("LoadingCategoryInBackground", MetadataCategoryName(categoryName), index + 1, orderedCategories.Length);
                await LoadMetadataPageAsync(categoryName, 0, updateStatus: false,
                    cancellationToken: metadataCancellation.Token, loadGeneration: loadGeneration);
                if (loadGeneration != _metadataLoadGeneration)
                    return;
                if (!_metadataNextOffsets.ContainsKey(categoryName))
                    failedCategories.Add(MetadataCategoryName(categoryName));
            }

            _statusText.Text = failedCategories.Count == 0
                ? T("FirstObjectsLoaded", MetadataPageSize, profile.Name)
                : T("MetadataLoadPartial", string.Join(", ", failedCategories));
            _metadataEmptyText.Text = T("NoObjects");
            AddMessage(failedCategories.Count == 0 ? "Success" : "Warning", T("DatabaseMetadata"),
                failedCategories.Count == 0
                    ? T("InitialMetadataLoaded", orderedCategories.Length, MetadataPageSize)
                    : T("InitialMetadataFailed", string.Join(", ", failedCategories)));
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
            {
                _metadataEmptyText.Text = T("ObjectsLoadFailed");
                ReportError(T("PreparingMetadata", profile.Name), exception);
            }
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
        _clearMetadataSearchButton.IsEnabled = _metadataSearchBox.IsEnabled && !string.IsNullOrEmpty(_metadataSearchBox.Text);
        var searchTerm = _metadataSearchBox.Text?.Trim() ?? string.Empty;
        if (SelectedConnection is not { } profile ||
            !_sessionManager.GetActiveSessions().Any(session => session.ProfileId == profile.Id))
            return;

        if (searchTerm.Length >= 2)
        {
            await SearchMetadataAsync(profile, searchTerm, debounce: true);
            return;
        }

        _metadataSearchCancellation?.Cancel();
        if (_isMetadataSearchActive)
        {
            _isMetadataSearchActive = false;
            await RefreshMetadataAsync(profile);
        }
        if (searchTerm.Length == 1 && string.Equals(_metadataSearchBox.Text?.Trim(), searchTerm, StringComparison.Ordinal))
            _statusText.Text = T("ObjectSearchMinLength");
    }

    private async Task SearchMetadataAsync(ConnectionProfile profile, string searchTerm, bool debounce)
    {
        _metadataSearchCancellation?.Cancel();
        var cancellation = new CancellationTokenSource();
        _metadataSearchCancellation = cancellation;
        var kindFilter = GetSelectedMetadataKindFilter();
        try
        {
            if (debounce)
                await Task.Delay(300, cancellation.Token);
            _metadataEmptyText.Text = T("SearchingObjects");
            _statusText.Text = T("SearchingObjectsFor", searchTerm);
            var results = await Task.Run(
                () => _sessionManager.SearchMetadataAsync(profile.Id, searchTerm, cancellation.Token, kindFilter),
                cancellation.Token);
            if (cancellation.IsCancellationRequested ||
                SelectedConnection is not { } currentProfile || currentProfile.Id != profile.Id ||
                !string.Equals(_metadataSearchBox.Text?.Trim(), searchTerm, StringComparison.Ordinal) ||
                !string.Equals(GetSelectedMetadataKindFilter(), kindFilter, StringComparison.Ordinal))
                return;

            CancelMetadataLoading();
            _isMetadataSearchActive = true;
            _metadataEmptyText.Text = T("NoMatchingObjects");
            _metadataObjects.Clear();
            UpdateActionAvailability();
            const int searchUiBatchSize = 50;
            for (var index = 0; index < results.Count; index += searchUiBatchSize)
            {
                cancellation.Token.ThrowIfCancellationRequested();
                if (SelectedConnection is not { } activeProfile ||
                    activeProfile.Id != profile.Id)
                    return;

                _metadataObjects.AddRange(results.Skip(index).Take(searchUiBatchSize).ToArray());
                await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            }
            cancellation.Token.ThrowIfCancellationRequested();

            var filterDescription = kindFilter is null
                ? T("ObjectsAndColumns")
                : MetadataCategoryName(kindFilter).ToLower(Culture);
            _statusText.Text = results.Count == 0
                ? T("NoObjectsMatchingTerm", filterDescription, searchTerm)
                : T("ObjectsMatchingTerm", results.Count, filterDescription, searchTerm);
        }
        catch (OperationCanceledException)
        {
            // A newer search term superseded this request.
        }
        catch (Exception exception)
        {
            if (!cancellation.IsCancellationRequested)
            {
                _metadataEmptyText.Text = T("ObjectsSearchFailed");
                ReportError(T("SearchingMetadata", searchTerm), exception);
            }
        }
        finally
        {
            if (ReferenceEquals(_metadataSearchCancellation, cancellation))
                _metadataSearchCancellation = null;
            cancellation.Dispose();
        }
    }

    private void ResetMetadataSearch()
    {
        _metadataSearchCancellation?.Cancel();
        _isMetadataSearchActive = false;
        _metadataSearchBox.Clear();
    }

    private string? GetSelectedMetadataKindFilter() =>
        (_metadataSearchKindFilter.SelectedItem as ComboBoxItem)?.Tag as string;

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
                T("OpenedFromColumn", column.Name)));
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
                "Tables", constraint.ParentName, null, T("OpenedFromConstraint", constraint.Name)));
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
                    relationKind, item.ParentName, null, T("OpenedFromMetadata", item.Kind.ToLowerInvariant(), item.Name)));
                return;
            case "Index segment" when item.ParentName is not null:
                await ShowMetadataObjectDetailsAsync(new DatabaseMetadataObject(
                    "Indexes", item.ParentName, null, T("OpenedFromIndex", item.Name)));
                return;
            case "Procedure parameter":
            case "Procedure parameter group":
            case "Procedure source":
                if (item.ParentName is not null)
                    await ShowMetadataObjectDetailsAsync(new DatabaseMetadataObject(
                        "Procedures", item.ParentName, null, T("OpenedFromMetadata", item.Kind.ToLowerInvariant(), item.Name)));
                return;
            case "Trigger source":
                if (item.ParentName is not null)
                    await ShowMetadataObjectDetailsAsync(new DatabaseMetadataObject(
                        "Triggers", item.ParentName, null, T("OpenedFromTrigger")));
                return;
            case "Domain properties":
            case "Domain default":
            case "Domain validation":
                if (item.ParentName is not null)
                    await ShowMetadataObjectDetailsAsync(new DatabaseMetadataObject(
                        "Domains", item.ParentName, null, T("OpenedFromMetadata", item.Kind.ToLowerInvariant(), item.Name)));
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
        if (item.Children is not null || SelectedConnection is not { } profile)
            return;

        try
        {
            var details = await Task.Run(
                () => _sessionManager.GetMetadataObjectDetailsAsync(profile.Id, item.Kind, item.Name));
            if (SelectedConnection is not { } currentProfile || currentProfile.Id != profile.Id)
                return;
            var description = item.Details;
            if (item.Kind == "Procedures" && details.Count == 0)
                description += T("DuplicateProcedureDetails");
            else if (item.Kind == "Indexes")
                description += T("IndexSegments", details.Count);
            else if (item.Kind == "Procedures")
                description += T("DetailGroups", details.Count);
            else if (item.Kind == "Triggers")
                description += T("SourceLoaded");

            UpdateMetadataObjectNode(item, item with { Details = description, Children = details });
            _statusText.Text = T("MetadataDetailsLoaded", details.Count, item.Kind.ToLowerInvariant(), item.Name);
        }
        catch (Exception exception)
        {
            ReportError(T("LoadingMetadataDetails", item.Kind, item.Name), exception);
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
        if (SelectedConnection is not { } profile ||
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
                ? T("LoadingCategory", categoryName)
                : T("LoadingMoreObjects", MetadataPageSize, categoryName.ToLowerInvariant());
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
                SelectedConnection is not { } currentProfile || currentProfile.Id != profile.Id)
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
                    SelectedConnection is not { } activeProfile || activeProfile.Id != profile.Id)
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
                    ? T("MoreObjectsLoaded", displayed, categoryName.ToLowerInvariant())
                    : T("AllObjectsLoaded", nextOffset, categoryName.ToLowerInvariant());

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
                ReportError(T("LoadingCategoryContext", categoryName), exception);
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
            _organizeMetadataMenuItem.IsEnabled = false;
            _moveCategoryUpMenuItem.IsEnabled = false;
            _moveCategoryDownMenuItem.IsEnabled = false;
            return;
        }

        var index = _metadataObjects.IndexOf(selected);
        _moveCategoryUpMenuItem.IsEnabled = index > 0;
        _moveCategoryDownMenuItem.IsEnabled = index >= 0 && index < _metadataObjects.Count - 1;
        _organizeMetadataMenuItem.IsEnabled = _moveCategoryUpMenuItem.IsEnabled || _moveCategoryDownMenuItem.IsEnabled;
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
        await SavePreferencesAsync(T("CategoryOrderSaved"));
    }

    private void ClearMessages()
    {
        _messages.Clear();
        _statusText.Text = T("MessagesCleared");
        _messagesView.ShowLatest();
    }

    private async Task ClearQueryHistoryAsync()
    {
        if (_isQueryExecuting)
        {
            ShowFooterNotification(T("WaitToClearHistory"), "information");
            return;
        }

        try
        {
            await _queryHistoryStore.SaveAsync(Array.Empty<QueryHistoryEntry>());
            _queryHistory.Clear();
            _statusText.Text = T("HistoryClearedStatus");
            ShowFooterNotification(T("HistoryCleared"), "success");
        }
        catch (Exception exception)
        {
            ReportError(T("ClearingHistory"), exception);
        }
    }

    private void ClearResults()
    {
        if (_isQueryExecuting)
        {
            ShowFooterNotification(T("WaitToClearResults"), "information");
            return;
        }

        _queryResults.Reset();
        _statusText.Text = T("ResultsClearedStatus");
        ShowFooterNotification(T("ResultsCleared"), "success");
    }

    private async Task CopyToClipboardAsync(string text, string errorContext, string successMessage)
    {
        if (Clipboard is not { } clipboard)
        {
            ReportError(errorContext, T("ClipboardUnavailable"));
            return;
        }

        try
        {
            await clipboard.SetTextAsync(text);
            ShowFooterNotification(successMessage, "success");
        }
        catch (Exception exception)
        {
            ReportError(errorContext, exception);
        }
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
            ReportInformation(T("SqlFormatting"), T("NoSqlToFormat"));
            return;
        }

        var formatted = FirebirdSqlFormatter.Format(source);
        if (string.Equals(source, formatted, StringComparison.Ordinal))
        {
            _statusText.Text = T("SqlAlreadyFormatted");
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
            ? T("SelectedSqlFormatted")
            : T("EditorSqlFormatted");
        ShowFooterNotification(T("SqlFormatted"), "success");
    }

    private async void OnExecuteClick(object? sender, RoutedEventArgs e) => await ExecuteSelectedSqlAsync();

    private async Task ExecuteSelectedSqlAsync()
    {
        if (SelectedConnection is not { } profile ||
            !_sessionManager.GetActiveSessions().Any(session => session.ProfileId == profile.Id))
        {
            ReportError(T("SqlExecution"), T("NoConnectionForSql"));
            return;
        }

        string sql;
        try
        {
            sql = GetSqlToExecute();
        }
        catch (InvalidOperationException exception)
        {
            ReportError(T("QueryCouldNotStart"), exception.Message);
            return;
        }

        if (string.IsNullOrWhiteSpace(sql))
        {
            ReportInformation(T("SqlExecution"), T("NoSqlToExecute"));
            return;
        }

        var parameterNames = FirebirdSqlParameterParser.FindNames(sql);
        IReadOnlyDictionary<string, object?>? parameterValues = null;
        if (parameterNames.Count > 0)
        {
            parameterValues = await ShowSqlParametersDialogAsync(parameterNames);
            if (parameterValues is null)
            {
                _statusText.Text = T("ParametersCancelled");
                return;
            }
        }

        var executionSql = FirebirdSqlParameterParser.NormalizePlaceholders(sql);
        var cancellation = new CancellationTokenSource();
        _queryExecutionCancellation = cancellation;
        _isQueryExecuting = true;
        _queryResults.ShowRunning();
        _statusText.Text = T("ExecutingOnProfile", profile.Name);
        UpdateActionAvailability();
        var succeeded = false;
        var cancelled = false;
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
                ? T("RowsReturnedInTime", result.Rows.Count, result.Elapsed.TotalMilliseconds)
                : result.RowsAffected >= 0
                    ? T("RowsAffectedInTime", result.RowsAffected, result.Elapsed.TotalMilliseconds)
                    : T("StatementTime", result.Elapsed.TotalMilliseconds);
            _queryResults.ShowResult(result);
            _statusText.Text = outcome;
            AddMessage("Success", T("SqlExecution"), outcome);
            if (ReferenceEquals(_resultsMessagesTabs.SelectedItem, _messagesTab))
                _resultsMessagesTabs.SelectedItem = _resultsTab;
            ShowFooterNotification(T("QuerySucceeded"), "success");
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
            outcome = T("QueryCancelledStatus");
            _queryResults.ShowCancelled();
            _statusText.Text = outcome;
            ReportInformation(T("SqlExecution"), outcome);
        }
        catch (Exception exception)
        {
            outcome = exception.Message;
            _queryResults.ShowFailed();
            ReportError(T("SqlExecutionRollback"), exception);
        }
        finally
        {
            _isQueryExecuting = false;
            _queryExecutionCancellation = null;
            cancellation.Dispose();
            var entry = new QueryHistoryEntry(
                DateTimeOffset.UtcNow, profile.Id, profile.Name, sql, succeeded, outcome)
            {
                Cancelled = cancelled
            };
            _queryHistory.Insert(0, entry);
            while (_queryHistory.Count > 250)
                _queryHistory.RemoveAt(_queryHistory.Count - 1);
            try
            {
                await _queryHistoryStore.SaveAsync(_queryHistory.ToArray());
            }
            catch (Exception historyException)
            {
                ReportError(T("SavingHistory"), historyException);
            }

            UpdateActionAvailability();
        }
    }

    private async Task<IReadOnlyDictionary<string, object?>?> ShowSqlParametersDialogAsync(IReadOnlyList<string> names)
    {
        (string Id, string Label)[] parameterTypes =
        [
            ("Text", T("ParameterText")), ("Integer", T("ParameterInteger")),
            ("Decimal", T("ParameterDecimal")), ("Date", T("ParameterDate")),
            ("Time", T("ParameterTime")), ("Timestamp", T("ParameterTimestamp")),
            ("Boolean", T("ParameterBoolean")), ("NULL", T("ParameterNULL"))
        ];
        var entries = new List<(string Name, TextBox Value, ComboBox Type)>();
        var fields = new StackPanel { Spacing = 8 };
        foreach (var name in names)
        {
            var value = new TextBox { PlaceholderText = T("ParameterPlaceholder", name), MinWidth = 210 };
            var type = new ComboBox
            {
                ItemsSource = parameterTypes.Select(option => new ComboBoxItem { Content = option.Label, Tag = option.Id }).ToArray(),
                SelectedIndex = 0,
                MinWidth = 115
            };
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
        var cancel = new Button { Content = T("Cancel"), MinWidth = 90 };
        var execute = new Button { Content = T("Execute"), MinWidth = 90 };
        var dialog = new Window
        {
            Title = T("SqlParameters"),
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
                            new TextBlock { Text = T("SqlParametersHelp"), TextWrapping = Avalonia.Media.TextWrapping.Wrap },
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
                {
                    var type = (entry.Type.SelectedItem as ComboBoxItem)?.Tag as string ?? "Text";
                    values[entry.Name] = ParseSqlParameterValue(entry.Value.Text ?? string.Empty, type);
                }
                dialog.Close(values);
            }
            catch (Exception exception) when (exception is FormatException or OverflowException)
            {
                error.Text = T("InvalidParameter", exception.Message);
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

    private void OnCancelQueryClick(object? sender, RoutedEventArgs e)
    {
        if (_queryExecutionCancellation is { IsCancellationRequested: false } cancellation)
        {
            _statusText.Text = T("CancellationRequested");
            _queryResults.ShowCancelling();
            cancellation.Cancel();
        }
    }

    private void RestoreQueryFromHistory(QueryHistoryEntry entry)
    {
        var editorTab = GetHistoryEditorTab();
        editorTab.Editor.Document.Replace(0, editorTab.Editor.Document.TextLength, entry.Sql);
        _editorTabs.SelectedItem = editorTab.Tab;
        editorTab.Editor.CaretOffset = 0;
        Dispatcher.UIThread.Post(() => editorTab.Editor.TextArea.Focus(), DispatcherPriority.Input);
        _statusText.Text = T("RestoredQuery", entry.ExecutedAt.ToLocalTime(), entry.ProfileName);
    }

    private SqlEditorState GetHistoryEditorTab() =>
        _sqlEditors.FirstOrDefault(item => ReferenceEquals(item.Tab, _lastActiveSqlTab)) ?? GetActiveSqlEditorTab();

    private async void OnSettingsClick(object? sender, RoutedEventArgs e)
    {
        var dialog = new SettingsWindow(_preferences, ((App)Avalonia.Application.Current!).Appearance);
        var changes = await dialog.ShowDialog<SettingsChanges?>(this);
        if (changes is null)
            return;

        var updatedPreferences = _preferences with
        {
            ExecuteSqlShortcut = changes.ExecuteShortcut.ToString(),
            InterfaceLanguage = changes.InterfaceLanguage,
            DownloadAllEmbeddedEngines = changes.DownloadAllEmbeddedEngines,
            LightThemeColors = changes.Light,
            DarkThemeColors = changes.Dark
        };
        if (updatedPreferences == _preferences)
            return;

        _preferences = updatedPreferences;
        ApplyTheme();
        ApplyExecuteSqlShortcut();
        await SavePreferencesAsync(T(changes.InterfaceLanguage == Culture.Name ? "SettingsSaved" : "SettingsSavedRestart"));
    }

    private void ApplyExecuteSqlShortcut()
    {
        var shortcut = _preferences.ExecuteSqlKey;
        _executeButton.InputGesture = new KeyGesture(shortcut);
        this.FindControl<TextBlock>("ExecuteShortcutText")!.Text = shortcut.ToString();
        ToolTip.SetTip(_executeQueryAction, GetSqlExecutionHint(shortcut));
    }

    private static string GetSqlExecutionHint(Key shortcut) =>
        T("ExecuteSqlHint", shortcut);

    private async Task SetThemeAsync(string theme)
    {
        if (theme is not ("Light" or "Dark"))
            return;

        _preferences = _preferences with { Theme = theme };
        ApplyTheme();
        await SavePreferencesAsync(T("ThemeChanged", ThemeName(theme)));
    }

    private void ApplyTheme()
    {
        var app = (App)Avalonia.Application.Current!;
        var overrides = _preferences.Theme == "Dark" ? _preferences.DarkThemeColors : _preferences.LightThemeColors;
        app.Appearance.Apply(_preferences.Theme, overrides);
        app.RequestedThemeVariant = _preferences.Theme switch
        {
            "Light" => ThemeVariant.Light,
            _ => ThemeVariant.Dark
        };

        foreach (var editor in _sqlEditors)
            editor.Editor.TextArea.TextView.Redraw();
        foreach (var editor in _readOnlySqlEditors)
            editor.TextArea.TextView.Redraw();
    }

    private void ApplySqlSyntaxHighlighting(AvaloniaEdit.TextEditor editor)
    {
        // AvaloniaEdit applies the selection foreground after syntax colors.
        editor.TextArea.TextView.LineTransformers.Insert(
            0, new FirebirdSqlSyntaxColorizer(() => _preferences.Theme == "Dark"));
    }

    private void ApplyConnectionDisplayMode()
    {
        var showDetails = _preferences.ConnectionDisplayMode == "Detailed";
        _connectionsList.Classes.Set("detailed", showDetails);
        this.FindControl<MenuItem>("ConnectionDisplayDetailedMenuItem")!.IsChecked = showDetails;
        this.FindControl<MenuItem>("ConnectionDisplayMinimalMenuItem")!.IsChecked = !showDetails;
        var toggleButton = this.FindControl<Button>("ToggleConnectionDisplayButton")!;
        toggleButton.Content = T(showDetails ? "Minimal" : "Detailed");
        ToolTip.SetTip(toggleButton, showDetails
            ? T("SwitchToCompact")
            : T("SwitchToDetailed"));
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
                ? T("DetailedModeEnabled")
                : T("MinimalModeEnabled");
        }
        catch (Exception exception)
        {
            _preferences = previousPreferences;
            ApplyConnectionDisplayMode();
            ReportError(T("SavingConnectionMode"), exception);
        }
    }

    private void SetHistoryDockMode(bool dockRight)
    {
        _historyDockRightMenuItem.IsChecked = dockRight;
        _historyDockTabMenuItem.IsChecked = !dockRight;
        if (_queryHistoryPanel.IsVisible != dockRight)
        {
            if (dockRight)
            {
                _dockedQueryHistory.SelectedEntry = _tabbedQueryHistory.SelectedEntry;
                if (ReferenceEquals(_editorTabs.SelectedItem, _queryHistoryTab))
                    _editorTabs.SelectedItem = GetHistoryEditorTab().Tab;
            }
            else
            {
                _tabbedQueryHistory.SelectedEntry = _dockedQueryHistory.SelectedEntry;
                if (!_isHistoryDockCollapsed)
                    _expandedHistoryDockWidth = _workspaceGrid.ColumnDefinitions[2].Width;
            }
        }
        ApplyHistoryDockLayout(dockRight);
    }

    private void SetHistoryDockCollapsed(bool collapse)
    {
        if (collapse)
            _expandedHistoryDockWidth = _workspaceGrid.ColumnDefinitions[2].Width;
        _isHistoryDockCollapsed = collapse;
        ApplyHistoryDockLayout(true);
        if (collapse)
            _expandHistoryDockButton.Focus();
        else
            _dockedQueryHistory.FocusCollapseAction();
    }

    private void ApplyHistoryDockLayout(bool dockRight)
    {
        var dockExpanded = dockRight && !_isHistoryDockCollapsed;
        _queryHistoryPanel.IsVisible = dockRight;
        _dockedQueryHistory.IsVisible = dockExpanded;
        _expandHistoryDockButton.IsVisible = dockRight && _isHistoryDockCollapsed;
        _historySplitter.IsVisible = dockExpanded;
        _queryHistoryTab.IsVisible = !dockRight;
        _workspaceGrid.ColumnDefinitions[1].Width = new GridLength(dockExpanded ? 5 : 0);
        var historyColumn = _workspaceGrid.ColumnDefinitions[2];
        historyColumn.MinWidth = dockExpanded ? 220 : 0;
        historyColumn.Width = dockExpanded ? _expandedHistoryDockWidth : new GridLength(dockRight ? 36 : 0);
    }

    private async void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.S && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            e.Handled = true;
            await SaveSqlEditorRecoveryNowAsync(T("EditorSaved"));
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

        if (e.Key != _executeButton.InputGesture?.Key || e.KeyModifiers != KeyModifiers.None || _isQueryExecuting)
            return;

        e.Handled = true;
        await ExecuteSelectedSqlAsync();
    }

    private async Task ShowSqlCompletionAsync()
    {
        if (SelectedConnection is not { } profile ||
            !_sessionManager.GetActiveSessions().Any(session => session.ProfileId == profile.Id))
        {
            _statusText.Text = T("ConnectForCompletion");
            return;
        }

        try
        {
            var editor = GetActiveSqlEditorTab().Editor;
            var caretOffset = editor.CaretOffset;
            var documentText = editor.Document.Text;
            if (!_completionMetadataCache.TryGetValue(profile.Id, out var schema))
            {
                _statusText.Text = T("LoadingCompletionMetadata");
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
                    _statusText.Text = T("LoadingCompletionDomains");
                    domains = await Task.Run(() => _sessionManager.GetSqlCompletionDomainsAsync(profile.Id));
                    _completionDomainCache[profile.Id] = domains;
                }
                completionSchema = schema.Concat(domains).ToArray();
            }

            if (SelectedConnection is not { } currentProfile || currentProfile.Id != profile.Id)
                return;

            var suggestions = SqlCompletionCatalog.Build(documentText, caretOffset, completionSchema);
            if (suggestions.Count == 0)
            {
                _statusText.Text = T("NoSqlSuggestions");
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
            _statusText.Text = T("SqlSuggestionsCount", suggestions.Count, profile.Name) +
                (domainSearchDeferred ? T("DomainSearchTip") : string.Empty);
        }
        catch (Exception exception)
        {
            ReportError(T("SqlAutocomplete"), exception);
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
                throw new InvalidOperationException(T("SelectOneStatement"));
            return selectedSql;
        }

        var documentText = editor.Document.Text;
        EnsureMultipleStatementsAreSeparated(documentText);
        var statements = SplitSqlStatements(documentText);
        if (statements.Count > 1)
            throw new InvalidOperationException(T("SelectCompleteStatement"));

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
            throw new InvalidOperationException(T("SeparateStatements"));
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

    private SqlEditorState GetActiveSqlEditorTab()
    {
        if (_editorTabs.SelectedItem is TabItem selected)
        {
            var active = _sqlEditors.FirstOrDefault(item => ReferenceEquals(item.Tab, selected));
            if (active is not null)
                return active;
        }

        return _sqlEditors[0];
    }

    private void ConfigureSqlEditorTab(TabItem tab, AvaloniaEdit.TextEditor editor)
    {
        tab.Tag = editor;
        tab.Classes.Add("sql-editor-tab");
        _sqlEditors.Add(new SqlEditorState(tab, editor));
        editor.Document.TextChanged += (_, _) =>
        {
            if (!_isRestoringEditors)
                ScheduleSqlEditorRecoverySave();
        };
        var renameItem = new MenuItem { Header = T("RenameEditorDialog") };
        renameItem.Click += async (_, _) => await RenameSqlEditorTabAsync(tab);
        var closeItem = new MenuItem { Header = T("CloseTab") };
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
            WordWrap = false,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
        };
        ApplySqlSyntaxHighlighting(editor);

        var tab = new TabItem { Header = T("NumberedEditor", number), Content = editor };
        ConfigureSqlEditorTab(tab, editor);
        var insertIndex = _sqlEditors.Count - 1;
        _editorTabs.Items.Insert(insertIndex, tab);
        _editorTabs.SelectedItem = tab;
        editor.Focus();
        ScheduleSqlEditorRecoverySave(showNotification: false);
    }

    private async Task RenameSqlEditorTabAsync(TabItem tab)
    {
        var currentName = tab.Header?.ToString() ?? T("SqlEditor");
        var nameInput = new TextBox { Text = currentName, Margin = new Avalonia.Thickness(0, 8, 0, 12) };
        var renameButton = new Button { Content = T("RenameEditor"), HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right, MinWidth = 90 };
        var dialog = new Window
        {
            Title = T("RenameSqlEditor"),
            Width = 360,
            Height = 145,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            Content = new StackPanel
            {
                Margin = new Avalonia.Thickness(14),
                Children = { new TextBlock { Text = T("EditorTitle") }, nameInput, renameButton }
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
        var cancelButton = new Button { Content = T("Cancel"), MinWidth = 90 };
        var closeButton = new Button { Content = T("CloseEditor"), MinWidth = 100 };
        var dialog = new Window
        {
            Title = T("CloseSqlEditor"),
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
                        Text = T("ConfirmCloseEditor", tab.Header),
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

    private async Task OpenSqlIdentifierAsync(AvaloniaEdit.TextEditor editor, int offset)
    {
        if (SelectedConnection is not { } profile ||
            !_sessionManager.GetActiveSessions().Any(session => session.ProfileId == profile.Id))
        {
            ShowFooterNotification(T("ConnectToNavigate"), "information");
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
                    relation.Kind == "View" ? "Views" : "Tables", relation.Name, null, T("OpenedFromSqlEditor")));
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
                    kind, databaseObject.Name, null, T("OpenedObjectFromEditor", databaseObject.Kind)));
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
                    T("OpenedFromField", identifier)));
            }
        }
        catch (Exception exception)
        {
            ReportError(T("NavigatingToObject", identifier), exception);
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
            Text = source ?? string.Empty,
            IsReadOnly = true,
            ShowLineNumbers = true,
            WordWrap = false,
            MinHeight = minHeight,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
        };
        ApplySqlSyntaxHighlighting(editor);
        _readOnlySqlEditors.Add(editor);
        return editor;
    }

    private async Task ShowMetadataObjectDetailsAsync(DatabaseMetadataObject item)
    {
        if (SelectedConnection is not { } profile ||
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
                Classes = { "section-title" }
            });
            if (!string.IsNullOrWhiteSpace(item.Details))
                panel.Children.Add(new TextBlock { Text = item.Details, Opacity = 0.75, TextWrapping = Avalonia.Media.TextWrapping.Wrap });

            if (details.Count == 0)
            {
                panel.Children.Add(new TextBlock
                {
                    Text = item.Kind == "Procedures"
                        ? T("AmbiguousProcedureMetadata")
                        : T("NoAdditionalMetadata"),
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
                if (detail.Kind is "Procedure source" or "Trigger source" or "View source" or "Domain default" or "Domain validation")
                {
                    panel.Children.Add(new ScrollViewer
                    {
                        MaxHeight = 400,
                        HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                        VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                        Content = CreateReadOnlySqlEditor(FirebirdDdlPreview.FormatSource(detail.Kind, detail.Details), 220)
                    });
                }
                else
                {
                    panel.Children.Add(new TextBlock
                    {
                        Text = detail.Details,
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
                Classes = { "inspector-tab" },
                Tag = new MetadataObjectTabIdentity(profile.Id, item.Kind, item.Name),
                Content = new ScrollViewer { Content = panel }
            };
            var closeItem = new MenuItem { Header = T("CloseTab") };
            closeItem.Click += (_, _) => CloseMetadataObjectTab(tab);
            tab.ContextMenu = new ContextMenu { Items = { closeItem } };
            EnableMiddleClickClose(tab, () => { CloseMetadataObjectTab(tab); return Task.CompletedTask; });
            var insertIndex = _sqlEditors.Count + 1;
            _editorTabs.Items.Insert(insertIndex, tab);
            _editorTabs.SelectedItem = tab;
        }
        catch (Exception exception)
        {
            ReportError(T("OpeningObject", item.Kind, item.Name), exception);
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
        "Procedures" => T("Procedure"),
        "Triggers" => T("Trigger"),
        "Indexes" => T("Index"),
        "Domains" => T("Domain"),
        _ => kind
    };

    private sealed record MetadataObjectTabIdentity(Guid ProfileId, string Kind, string Name);

    private async Task ShowTableDetailsAsync(DatabaseMetadataObject table)
    {
        if (SelectedConnection is not { } profile ||
            !_sessionManager.GetActiveSessions().Any(session => session.ProfileId == profile.Id))
            return;

        if (string.Equals(_currentTableDetailsName, table.Name, StringComparison.Ordinal) &&
            _tableDetailsTab.IsVisible)
        {
            _editorTabs.SelectedItem = _tableDetailsTab;
            return;
        }

        _tableDetailsTab.Header = $"{T(table.Kind == "Views" ? "View" : "Table")}: {table.Name}";
        _tableDetailsTab.IsVisible = true;
        _editorTabs.SelectedItem = _tableDetailsTab;
        _tableDetailsTitle.Text = T("LoadingObjectDetails", table.Name);
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
        _tableDataPageStatus.Text = T("Loading");

        try
        {
            var details = await _sessionManager.GetMetadataObjectDetailsAsync(profile.Id, table.Kind, table.Name);
            if (!string.Equals(_currentTableDetailsName, table.Name, StringComparison.Ordinal) ||
                SelectedConnection is not { } currentProfile || currentProfile.Id != profile.Id)
                return;

            UpdateMetadataObjectNode(table, table with { Children = details });
            var fields = details.Where(item => item.Kind == "Column").ToArray();
            var fieldRows = fields.Select(item => new TableFieldDisplay(
                item.Name,
                FirebirdDdlPreview.ReadDetail(item.Details, "Domain"),
                FirebirdDdlPreview.DescribeFieldType(item.Details),
                item.Details.Contains("not null", StringComparison.OrdinalIgnoreCase) ? "No" : "Yes",
                FirebirdDdlPreview.ReadDetail(item.Details, "position") ?? "?")).ToArray();
            _tableFieldsGrid.ItemsSource = fieldRows;
            var constraints = details.FirstOrDefault(item => item.Kind == "Constraint group")?.Children
                ?? Array.Empty<DatabaseMetadataObject>();
            _tableConstraintsList.ItemsSource = constraints;
            _tableDdlText.Text = table.Kind == "Tables"
                ? FirebirdDdlPreview.BuildTableDdl(table.Name, fields, constraints)
                : FirebirdDdlPreview.FormatSource("View source", details.FirstOrDefault(item => item.Kind == "View source")?.Details
                    ?? "-- View definition is not available.");
            _tableDdlText.TextArea.TextView.Redraw();
            _tableDetailsTitle.Text = T("TableDetailsSummary", table.Name, fields.Length, constraints.Count);
            await LoadTableDataPageAsync(profile, table.Name, 0, null, false);
        }
        catch (Exception exception)
        {
            ReportError(T("LoadingTableDetails", table.Name), exception);
            _tableDataPageStatus.Text = T("LoadFailed");
        }
    }

    private async Task ChangeTableDataPageAsync(int direction)
    {
        if (SelectedConnection is not { } profile || string.IsNullOrWhiteSpace(_currentTableDetailsName))
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
        _tableDataPageStatus.Text = T("LoadingRows", offset + 1, offset + TableDataPageSize);
        try
        {
            var page = await _sessionManager.GetTableDataPageAsync(
                profile.Id, tableName, offset, TableDataPageSize, cursorValues, previous);
            if (!string.Equals(_currentTableDetailsName, tableName, StringComparison.Ordinal) ||
                SelectedConnection is not { } currentProfile || currentProfile.Id != profile.Id)
                return;

            ResultTable.Populate(_tableDataGrid, page.Columns, page.Rows);
            _currentTableDataOffset = offset;
            _currentTableDataPage = page;
            _previousTableDataPageButton.IsEnabled = page.HasPrevious;
            _nextTableDataPageButton.IsEnabled = page.HasNext;
            var first = page.Rows.Count == 0 ? 0 : offset + 1;
            var last = offset + page.Rows.Count;
            _tableDataPageStatus.Text = page.Rows.Count == 0
                ? T("NoRowsOnPage")
                : T("PageRows", first, last, TableDataPageSize) +
                    (page.HasStableOrder ? T("PrimaryKeySeek") : T("OffsetPaging")) +
                    (page.HasNext ? T("MoreRowsAvailable") : T("EndOfRows"));
            _statusText.Text = T("RowsLoaded", page.Rows.Count, tableName, offset);
        }
        catch (Exception exception)
        {
            _tableDataPageStatus.Text = T("LoadFailed");
            ReportError(T("LoadingTableData", tableName), exception);
        }
    }

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
            ReportError(T("SavingPreferences"), exception);
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
                showNotification ? T("EditorAutosaved") : null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            if (IsVisible)
                ReportError(T("SavingEditorRecovery"), exception);
        }
        finally
        {
            if (ReferenceEquals(_recoverySaveCancellation, cancellation))
                _recoverySaveCancellation = null;
            cancellation.Dispose();
        }
    }

    private async Task SaveSqlEditorRecoveryNowAsync(string? confirmation)
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
            ReportError(T("SavingEditorRecovery"), exception);
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
            _sqlEditors.Select(item => item.Tab.Header?.ToString() ?? T("SqlEditor")).ToArray(),
            _sqlEditors.Select(item => item.Editor.Text ?? string.Empty).ToArray(),
            Math.Max(0, activeIndex));
        await _sqlEditorRecoveryStore.SaveAsync(snapshot, cancellationToken);
        if (!string.IsNullOrWhiteSpace(confirmation))
            ShowFooterNotification(confirmation, "success");
    }

    private async Task RestoreSqlEditorRecoveryAsync()
    {
        var snapshot = await _sqlEditorRecoveryStore.LoadAsync();
        if (snapshot is null)
            return;

        if (snapshot.Version != 1 || snapshot.Queries.Length == 0 ||
            snapshot.Queries.Length > 50 || snapshot.Titles.Length != snapshot.Queries.Length)
        {
            ReportError(T("SqlEditorRecovery"), T("InvalidEditorRecovery"));
            return;
        }

        _isRestoringEditors = true;
        try
        {
            for (var index = 0; index < snapshot.Queries.Length; index++)
            {
                SqlEditorState editorTab;
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
                    ? T("NumberedEditor", index + 1)
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
            T("SavedSqlWorkspace"),
            T("WorkspaceRestored", snapshot.Queries.Length, snapshot.SavedAt.ToLocalTime()));
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
        _statusText.Text = T("OperationFailed", context);
        _resultsMessagesTabs.SelectedItem = _messagesTab;
        _messagesView.ShowLatest();
        ShowFooterNotification(T("OperationError", context), "error");
    }

    private void ReportInformation(string context, string details)
    {
        AddMessage("Information", context, details);
        _statusText.Text = details;
        _resultsMessagesTabs.SelectedItem = _messagesTab;
        _messagesView.ShowLatest();
        ShowFooterNotification(details, "information");
    }

    private void AddMessage(string severity, string context, string details)
    {
        const int maximumDetailsCharacters = 30_000;
        if (details.Length > maximumDetailsCharacters)
            details = details[..maximumDetailsCharacters] + Environment.NewLine + T("DetailsTruncated");
        _messages.Insert(0, new StudioMessageEntry(DateTimeOffset.Now, severity, context, details));
        while (_messages.Count > 500)
            _messages.RemoveAt(_messages.Count - 1);
    }

    private void ShowFooterNotification(string message, string severity)
    {
        _footerNotificationCancellation?.Cancel();
        var cancellation = new CancellationTokenSource();
        _footerNotificationCancellation = cancellation;
        _footerNotificationText.Text = message;
        _footerNotificationText.Classes.Clear();
        _footerNotificationText.Classes.Add(severity);
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
        var connectedProfileIds = _sessionManager.GetActiveSessions().Select(session => session.ProfileId).ToHashSet();
        foreach (var row in _connectionItems)
            row.UpdateConnectionState(connectedProfileIds.Contains(row.Profile.Id));

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
            ReportError(T("SavingWorkspaceForUpdate"), exception);
        }

        _recoverySaveCancellation?.Cancel();

        try
        {
            await ((IAsyncDisposable)_sessionManager).DisposeAsync();
        }
        catch (Exception exception)
        {
            ReportError(T("ClosingSessionsForUpdate"), exception);
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
            ReportError(T("SavingWorkspaceForExit"), exception);
        }

        _recoverySaveCancellation?.Cancel();

        try
        {
            await ((IAsyncDisposable)_sessionManager).DisposeAsync();
        }
        catch (Exception exception)
        {
            ReportError(T("ClosingSessions"), exception);
        }
    }

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
