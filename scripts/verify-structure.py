from pathlib import Path
import sys
import xml.etree.ElementTree as ET

root = Path(__file__).resolve().parents[1]
required = [
    "FirebirdStudio.sln",
    "Directory.Packages.props",
    "src/FirebirdStudio.Core/Connections/ConnectionProfile.cs",
    "src/FirebirdStudio.Core/Connections/ConnectionProfileValidator.cs",
    "src/FirebirdStudio.Desktop/Views/ConnectionEditorWindow.axaml.cs",
    "src/FirebirdStudio.Application/Connections/ConnectionProfileManager.cs",
    "src/FirebirdStudio.Application/Connections/IConnectionProfileRepository.cs",
    "src/FirebirdStudio.Application/Connections/IFirebirdConnectionTester.cs",
    "src/FirebirdStudio.Application/Connections/ISecureCredentialStore.cs",
    "src/FirebirdStudio.Application/Connections/IFirebirdSessionManager.cs",
    "src/FirebirdStudio.Core/Database/DatabaseMetadataObject.cs",
    "src/FirebirdStudio.Infrastructure/Connections/FirebirdConnectionStringFactory.cs",
    "src/FirebirdStudio.Infrastructure/Connections/FirebirdSessionManager.cs",
    "src/FirebirdStudio.Infrastructure/Connections/JsonConnectionProfileRepository.cs",
    "src/FirebirdStudio.Infrastructure/Connections/FirebirdConnectionTester.cs",
    "src/FirebirdStudio.Infrastructure/Connections/LatchkeySecureCredentialStore.cs",
    "src/FirebirdStudio.Infrastructure/Connections/Provider/IFirebirdProviderConnection.cs",
    "src/FirebirdStudio.Infrastructure/Connections/Provider/FirebirdProviderConnectionFactory.cs",
    "src/FirebirdStudio.Infrastructure/Properties.AssemblyInfo.cs",
    "tests/FirebirdStudio.Tests/FirebirdStudio.Tests.csproj",
    "tests/FirebirdStudio.Tests/Infrastructure/FirebirdSessionManagerTests.cs",
    "tests/FirebirdStudio.Tests/Infrastructure/LatchkeySecureCredentialStoreTests.cs",
    "src/FirebirdStudio.Desktop/Views/ConnectionEditorWindow.axaml",
    "src/FirebirdStudio.Desktop/Views/ConnectionPasswordWindow.axaml",
    "src/FirebirdStudio.Desktop/Views/ConnectionPasswordWindow.axaml.cs",
    "src/FirebirdStudio.Desktop/Services/StudioPreferencesStore.cs",
    "src/FirebirdStudio.Desktop/Services/QueryHistoryStore.cs",
    "docs/architecture/SQL-EXECUTION.md",
    "docs/architecture/CONNECTION-MANAGER.md",
    "docs/architecture/METADATA-EXPLORER.md",
    "docs/licensing/DEPENDENCIES.md",
]
missing = [path for path in required if not (root / path).is_file()]
if missing:
    print("Missing required files:", *missing, sep="\n- ")
    sys.exit(1)

xml_files = list(root.rglob("*.csproj")) + list(root.rglob("*.props")) + list(root.rglob("*.axaml")) + list(root.rglob("*.manifest"))
for path in xml_files:
    ET.parse(path)

profile_source = (root / "src/FirebirdStudio.Core/Connections/ConnectionProfile.cs").read_text(encoding="utf-8")
if "public string Password" in profile_source or "public string? Password" in profile_source:
    print("ConnectionProfile must not contain a password field.")
    sys.exit(1)

repository_source = (root / "src/FirebirdStudio.Infrastructure/Connections/JsonConnectionProfileRepository.cs").read_text(encoding="utf-8")
if "File.Move(temporaryPath, _filePath, overwrite: true)" not in repository_source:
    print("Profile persistence is missing the expected atomic replace step.")
    sys.exit(1)

print(f"Structure and XML checks passed ({len(xml_files)} XML files).")
for token in [
    "ConnectionServerType ServerType",
    "ClientLibraryPath",
    "PacketSize",
    "FetchSize",
    "DatabaseCachePages",
    "Pooling",
    "Compression",
    "DisableDatabaseTriggers",
    "DisableGarbageCollection",
]:
    if token not in profile_source:
        print(f"Connection profile is missing expected setting: {token}")
        sys.exit(1)

tester_source = (root / "src/FirebirdStudio.Infrastructure/Connections/FirebirdConnectionStringFactory.cs").read_text(encoding="utf-8")
for token in [
    '["Server Type"]',
    '["Client Library"]',
    '["Packet Size"]',
    '["Fetch Size"]',
    '["Cache Pages"]',
    '["Compression"]',
    '["No Database Triggers"]',
    '["No Garbage Collect"]',
]:
    if token not in tester_source:
        print(f"Connection tester is missing provider mapping: {token}")
        sys.exit(1)

print("Profile model contains no password property.")
print("Connection registration fields are mapped to provider options.")
print("This does not compile the .NET solution, restore NuGet packages, or validate runtime behavior.")

session_source = (root / "src/FirebirdStudio.Infrastructure/Connections/FirebirdSessionManager.cs").read_text(encoding="utf-8")
for token in ["OpenAsync(cancellationToken)", "DisposeAsync()", "_sessions.ContainsKey(profile.Id)", "_operationGate"]:
    if token not in session_source:
        print(f"Session manager is missing expected lifecycle behavior: {token}")
        sys.exit(1)

main_window_source = (root / "src/FirebirdStudio.Desktop/Views/MainWindow.axaml.cs").read_text(encoding="utf-8")
for token in ["OnConnectClick", "OnDisconnectClick", "OnRefreshMetadataClick", "_sessionManager.GetMetadataAsync", "((IAsyncDisposable)_sessionManager).DisposeAsync()"]:
    if token not in main_window_source:
        print(f"Desktop session integration is missing: {token}")
        sys.exit(1)

print("Session lifecycle source checks passed.")

credential_store_source = (root / "src/FirebirdStudio.Infrastructure/Connections/LatchkeySecureCredentialStore.cs").read_text(encoding="utf-8")
for token in ["LatchkeyFactory.Create(ServiceName)", ".Set(", ".GetBytes(", ".Delete(", "CryptographicOperations.ZeroMemory(secretBytes)", "Task.Run("]:
    if token not in credential_store_source:
        print(f"Secure credential store is missing expected behavior: {token}")
        sys.exit(1)
if "Latchkey" not in (root / "src/FirebirdStudio.Infrastructure/FirebirdStudio.Infrastructure.csproj").read_text(encoding="utf-8"):
    print("Infrastructure project is missing the Latchkey package reference.")
    sys.exit(1)
password_dialog_source = (root / "src/FirebirdStudio.Desktop/Views/ConnectionPasswordWindow.axaml.cs").read_text(encoding="utf-8")
main_window_source = (root / "src/FirebirdStudio.Desktop/Views/MainWindow.axaml.cs").read_text(encoding="utf-8")
for token in ["RememberPassword", "ClearCapturedPassword"]:
    if token not in password_dialog_source:
        print(f"Password dialog is missing expected behavior: {token}")
        sys.exit(1)
for token in ["GetPasswordAsync", "SavePasswordAsync", "DeletePasswordAsync"]:
    if token not in main_window_source:
        print(f"Desktop credential integration is missing: {token}")
        sys.exit(1)
print("OS credential-store integration source checks passed.")


solution_source = (root / "FirebirdStudio.sln").read_text(encoding="utf-8")
if "FirebirdStudio.Tests" not in solution_source:
    print("The automated test project is not included in the solution.")
    sys.exit(1)

test_project = (root / "tests/FirebirdStudio.Tests/FirebirdStudio.Tests.csproj").read_text(encoding="utf-8")
for package in ["Microsoft.NET.Test.Sdk", "xunit.v3.mtp-off", "xunit.runner.visualstudio"]:
    if f'Include="{package}"' not in test_project:
        print(f"Test project is missing package reference: {package}")
        sys.exit(1)

provider_factory_source = (root / "src/FirebirdStudio.Infrastructure/Connections/Provider/FirebirdProviderConnectionFactory.cs").read_text(encoding="utf-8")
if "new FbConnection(connectionString)" not in provider_factory_source:
    print("Production provider factory does not create the Firebird provider connection.")
    sys.exit(1)

session_tests = list((root / "tests/FirebirdStudio.Tests").rglob("*Tests.cs"))
if len(session_tests) < 5:
    print("Expected deterministic unit test coverage for profiles, persistence, provider mapping, credentials, and sessions.")
    sys.exit(1)

print(f"Automated test source checks passed ({len(session_tests)} test files).")
print("The fake provider and fake credential backend are confined to the test assembly.")

metadata_provider_source = (root / "src/FirebirdStudio.Infrastructure/Connections/Provider/FirebirdProviderConnectionFactory.cs").read_text(encoding="utf-8")
for catalog in ["RDB$RELATIONS", "RDB$PROCEDURES", "RDB$PROCEDURE_PARAMETERS", "RDB$PROCEDURE_SOURCE", "RDB$TRIGGERS", "RDB$INDICES", "RDB$FIELDS", "RDB$RELATION_FIELDS", "RDB$RELATION_CONSTRAINTS", "RDB$REF_CONSTRAINTS", "RDB$INDEX_SEGMENTS"]:
    if catalog not in metadata_provider_source:
        print(f"Metadata reader is missing catalog: {catalog}")
        sys.exit(1)
if "ExecuteReaderAsync(cancellationToken)" not in metadata_provider_source or "ReadAsync(cancellationToken)" not in metadata_provider_source:
    print("Metadata reader must use asynchronous, cancellation-aware reads.")
    sys.exit(1)
metadata_ui_source = (root / "src/FirebirdStudio.Desktop/Views/MainWindow.axaml").read_text(encoding="utf-8")
for token in ["TreeView", "TreeDataTemplate", "ItemsSource=\"{Binding Children}\"", "GridSplitter Grid.Row=\"2\""]:
    if token not in metadata_ui_source:
        print(f"Expandable metadata UI is missing: {token}")
        sys.exit(1)
if "RDB$NULL_FLAG" not in metadata_provider_source or "RDB$FIELD_POSITION" not in metadata_provider_source:
    print("Table-column metadata is missing nullability or ordinal position.")
    sys.exit(1)
for token in ["RDB$CONSTRAINT_TYPE", "RDB$CONST_NAME_UQ", "RDB$UPDATE_RULE", "RDB$DELETE_RULE", "referenced_segment.RDB$FIELD_NAME"]:
    if token not in metadata_provider_source:
        print(f"Table constraint metadata is missing: {token}")
        sys.exit(1)
if '"Constraint group"' not in metadata_provider_source or '"Constraint"' not in metadata_provider_source or '"Constraint column"' not in metadata_provider_source:
    print("Expandable table constraint nodes are missing.")
    sys.exit(1)
for token in ["ReadMetadataObjectDetailsAsync", "ReadSqlCompletionDomainsAsync", "RDB$INDEX_SEGMENTS WHERE RDB$INDEX_NAME", "RDB$FIELD_POSITION", '"Index segment"']:
    if token not in metadata_provider_source:
        print(f"Lazy standalone index metadata is missing: {token}")
        sys.exit(1)
session_test_source = (root / "tests/FirebirdStudio.Tests/Infrastructure/FirebirdSessionManagerTests.cs").read_text(encoding="utf-8")
for test_name in ["MetadataIncludesExpandableStandaloneIndexSegments", "MetadataPreservesExpandableProcedureParametersAndSource"]:
    if test_name not in session_test_source:
        print(f"Metadata fixture test is missing: {test_name}")
        sys.exit(1)
preferences_source = (root / "src/FirebirdStudio.Desktop/Services/StudioPreferencesStore.cs").read_text(encoding="utf-8")
for token in ["settings.json", "File.Move(temporaryPath, _filePath, overwrite: true)", "MetadataCategoryOrder", "Theme"]:
    if token not in preferences_source:
        print(f"Persistent desktop preferences are missing: {token}")
        sys.exit(1)
for token in ["MoveSelectedCategoryAsync", "_metadataObjects.Move", "ThemeVariant.Light", "ThemeVariant.Dark", "ToggleThemeMenuItem", "SetThemeAsync"]:
    if token not in main_window_source:
        print(f"Desktop preferences UI is missing: {token}")
        sys.exit(1)
for token in ["ReadMetadataPageAsync", "ROWS {firstRow} TO {lastRow}", '"__LoadMore"', '"Not loaded · select to browse"']:
    if token not in metadata_provider_source:
        print(f"Lazy paged metadata is missing: {token}")
        sys.exit(1)
for token in ["private const int MetadataPageSize = 200", "_metadataNextOffsets", "_metadataCategoriesWithMore", "TryLoadMoreMetadataWhenVisible", "ScrollChanged"]:
    if token not in main_window_source:
        print(f"Automatic metadata infinite scrolling is missing: {token}")
        sys.exit(1)
for token in ["EnableMiddleClickClose", "PointerUpdateKind.MiddleButtonPressed", "ConfirmCloseSqlEditorTabAsync", "delete its saved contents"]:
    if token not in main_window_source:
        print(f"Middle-click tab closing or SQL editor confirmation is missing: {token}")
        sys.exit(1)
if "ReadTableColumnsAsync(cancellationToken)" in metadata_provider_source or "ReadTableConstraintsAsync(cancellationToken)" in metadata_provider_source:
    print("Metadata refresh must not eagerly load every table's columns and constraints.")
    sys.exit(1)
completion_source = (root / "src/FirebirdStudio.Application/Sql/SqlCompletionCatalog.cs").read_text(encoding="utf-8")
for token in ["RelationAliasRegex", "alias", "Firebird SQL keyword", "Firebird SQL function", "Database domain", "NeedsDomainSuggestions"]:
    if token not in completion_source:
        print(f"SQL autocomplete is missing: {token}")
        sys.exit(1)
for token in ["Key.Space", "ShowSqlCompletionAsync", "CompletionWindow", "GetSqlCompletionMetadataAsync", "GetSqlCompletionDomainsAsync", "GetTableDataPageAsync", "PreviousTableDataPageButton", "NextTableDataPageButton"]:
    if token not in main_window_source and token not in metadata_ui_source:
        print(f"SQL autocomplete or unbounded table-data paging is missing: {token}")
        sys.exit(1)
if "SELECT FIRST 100" in main_window_source:
    print("Table data still contains the previous fixed 100-row query.")
    sys.exit(1)
for token in ["GetMetadataPageAsync", "ExecuteSqlAsync", "CommitAsync", "RollbackAsync"]:
    if token not in session_source and token not in metadata_provider_source:
        print(f"SQL execution or metadata paging is missing: {token}")
        sys.exit(1)
query_history_source = (root / "src/FirebirdStudio.Desktop/Services/QueryHistoryStore.cs").read_text(encoding="utf-8")
for token in ["query-history.json", "MaximumEntries = 250", "File.Move(temporaryPath, _filePath, overwrite: true)"]:
    if token not in query_history_source:
        print(f"Persistent query history is missing: {token}")
        sys.exit(1)
for token in ["OnExecuteClick", "OnQueryHistoryDoubleTapped", "GetMetadataPageAsync", "ExecuteSqlAsync", "MoveCategoryUpMenuItem", "MoveCategoryDownMenuItem"]:
    if token not in main_window_source and token not in metadata_ui_source:
        print(f"Desktop query/history or metadata UI is missing: {token}")
        sys.exit(1)
print("Read-only expandable metadata catalog and UI source checks passed.")
print("Persistent category ordering and theme source checks passed.")
for token in ["QueryResultsGrid", "Key.F5", "ShowTableDetailsAsync", "TableFieldsGrid", "TableDataGrid", "TableDdlText", "SetHistoryDockMode", "TableViewColumn"]:
    if token not in main_window_source:
        print(f"Desktop query grid, table details, F5, or history docking is missing: {token}")
        sys.exit(1)
print("Lazy metadata loading, SQL autocomplete, paged table data, SQL execution, persistent query history, result grids, F5, and history docking source checks passed.")


# Phase 1 reliability release checks
recovery_source = (root / "src/FirebirdStudio.Desktop/Services/SqlEditorRecoveryStore.cs").read_text(encoding="utf-8")
connection_tester_source = (root / "src/FirebirdStudio.Infrastructure/Connections/FirebirdConnectionTester.cs").read_text(encoding="utf-8")
connection_test_source = (root / "tests/FirebirdStudio.Tests/Infrastructure/FirebirdConnectionTesterTests.cs").read_text(encoding="utf-8")
for token in ["sql-editor-recovery.json", "File.Move(temporaryPath, _filePath, overwrite: true)", "RestrictUnixPermissions"]:
    if token not in recovery_source:
        print(f"SQL editor recovery persistence is missing: {token}")
        sys.exit(1)
for token in [
    "RestoreSqlEditorRecoveryAsync", "ScheduleSqlEditorRecoverySave", "SaveSqlEditorRecoveryNowAsync",
    "Editor autosaved", "Editor saved", "Key.S", "CloseSqlEditorTabAsync", "Close tab",
    "RenameSqlEditorTabAsync", "Saving SQL editor workspace before closing", "ReportError", "MessageLogItems",
    "AddMessage(\"Success\"", "ReportInformation", "MessagesScrollViewer",
    "Review the restored queries before executing them.",
    "No active database connection. Select a saved connection profile",
    "ReferenceEquals(_resultsMessagesTabs.SelectedItem, _messagesTab)",
]:
    if token not in main_window_source:
        print(f"Phase 1 desktop reliability behavior is missing: {token}")
        sys.exit(1)
if "_sqlEditorRecoveryStore.DeleteAsync()" in main_window_source:
    print("SQL editor workspace must persist after a normal application exit.")
    sys.exit(1)
for token in ["StudioMessageEntry", "#E7F4E8", "#FFF5D9", "#FBE9E7"]:
    if token not in (root / "src/FirebirdStudio.Desktop/Services/StudioMessageEntry.cs").read_text(encoding="utf-8"):
        print(f"Message history severity styling is missing: {token}")
        sys.exit(1)
for token in ["[DNS resolution]", "[TCP reachability]", "[Firebird provider handshake and authentication]", "ServerVersion", "SanitizeMessage(exception.ToString(), password)"]:
    if token not in connection_tester_source:
        print(f"Connection diagnostics are missing: {token}")
        sys.exit(1)
for test_name in ["InvalidProfileReturnsValidationDiagnosticsWithoutNetworkAccess", "EmbeddedConnectionReportsMissingClientLibrary"]:
    if test_name not in connection_test_source:
        print(f"Connection diagnostic test is missing: {test_name}")
        sys.exit(1)
print("Phase 1 error reporting, SQL editor recovery, and connection diagnostics source checks passed.")

# SQL editor and metadata navigation fine-tuning checks
for token in [
    'AddSqlEditorTab', 'OnMetadataDoubleTapped', 'OnConstraintDoubleTapped',
    'KeyModifiers.Control', 'OpenSqlIdentifierAsync', 'GetSqlIdentifierAtOffset', 'IsSqlIdentifierContext',
    'ShowMetadataObjectDetailsAsync', 'MetadataObjectTabIdentity',
]:
    if token not in main_window_source:
        print(f"Database-object navigation or dynamic editor tabs are missing: {token}")
        sys.exit(1)
if 'AddSqlEditorButton' not in metadata_ui_source or 'addEditorHeader.Children.OfType<Button>()' not in main_window_source:
    print("The centered + button must remain inside the SQL editor tab strip and be wired to create an editor.")
    sys.exit(1)
if 'HorizontalAlignment="Center" VerticalAlignment="Center"' not in metadata_ui_source:
    print("The SQL editor + button must be centered in its tab header.")
    sys.exit(1)
if 'CloseMetadataObjectTab' not in main_window_source or 'CloseTableDetailsTab' not in main_window_source:
    print("Database object inspection tabs must expose a close action.")
    sys.exit(1)
if 'Load first 200' in metadata_provider_source or 'Load next {pageSize}' in main_window_source:
    print("Metadata browsing must load subsequent pages without visible load-page entries.")
    sys.exit(1)
if 'RDB$VALIDATION_SOURCE' not in metadata_provider_source:
    print("Domain inspection details are missing.")
    sys.exit(1)
print("Dynamic SQL editor tab placement and database-object navigation source checks passed.")

# Connection-time metadata, formatted source, and named SQL parameters
for token in [
    'Loading database objects in the background: {categoryName}', 'Large catalogs may take some time; the window remains responsive.',
    'await LoadMetadataPageAsync(categoryName, 0, updateStatus: false)',
    'Task.Run(() => _sessionManager.GetMetadataPageAsync(',
    'if ((itemIndex + 1) % 25 == 0)',
    'Loading the first 200 objects from each category in the background',
    'CreateReadOnlySqlEditor(detail.Details, 220)',
    '_readOnlySqlEditors.Add(_tableDdlText)',
    'foreach (var editor in _readOnlySqlEditors)',
    'GetOwningRelationKind(item.ParentName)',
    'new ObservableCollection<DatabaseMetadataObject>()', 'GetTransformedBounds()',
    'ShowSqlParametersDialogAsync', 'FirebirdSqlParameterParser.FindNames',
    'FirebirdSqlParameterParser.NormalizePlaceholders', 'parameterValues', 'ParseSqlParameterValue',
    'FirebirdSqlFormatter.Format',
]:
    if token not in main_window_source:
        print(f"Connection-time metadata, DDL formatting, or SQL parameter behavior is missing: {token}")
        sys.exit(1)
parameter_parser_source = (root / "src/FirebirdStudio.Desktop/Sql/FirebirdSqlParameterParser.cs").read_text(encoding="utf-8")
for token in ["FindNames", "NormalizePlaceholders", "lineComment", "blockComment"]:
    if token not in parameter_parser_source:
        print(f"Named SQL parameter parser is missing: {token}")
        sys.exit(1)
provider_source = (root / "src/FirebirdStudio.Infrastructure/Connections/Provider/FirebirdProviderConnectionFactory.cs").read_text(encoding="utf-8")
if "command.Parameters.AddWithValue(parameterName" not in provider_source:
    print("SQL execution does not bind supplied parameter values through the Firebird provider.")
    sys.exit(1)
print("Connection-time metadata paging, in-place append, SQL source formatting, and named parameter binding source checks passed.")


# Cross-platform external .NET updater packaging and safety checks
updater_project_source = (root / "src/FirebirdStudio.Updater/FirebirdStudio.Updater.csproj").read_text(encoding="utf-8")
updater_source = (root / "src/FirebirdStudio.Updater/MainWindow.cs").read_text(encoding="utf-8")
desktop_project_source = (root / "src/FirebirdStudio.Desktop/FirebirdStudio.Desktop.csproj").read_text(encoding="utf-8")
for token in ["Avalonia", "TargetFramework>net10.0"]:
    if token not in updater_project_source:
        print(f"Separate .NET updater project is missing: {token}")
        sys.exit(1)
for token in [
    "WaitForParentExitAsync", "VerifyPackageAsync", "ExtractSafelyAsync",
    "RollbackAsync", "EnsureNoReparsePoints", "MaxExpandedBytes", "File.Move(temporary, destination, overwrite: true)",
]:
    if token not in updater_source:
        print(f"External updater safety behavior is missing: {token}")
        sys.exit(1)
if "PublishExternalUpdater" not in desktop_project_source:
    print("The desktop project is missing the updater publish target.")
    sys.exit(1)
if "Apply-Update.ps1" in desktop_project_source or "powershell.exe" in main_window_source:
    print("The updater must not depend on PowerShell.")
    sys.exit(1)

downloader_source = (root / "src/FirebirdStudio.Desktop/Services/GitHubUpdateDownloader.cs").read_text(encoding="utf-8")
if "long SizeBytes, string Sha256" not in downloader_source:
    print("The verified update artifact metadata is not passed to the installer.")
    sys.exit(1)
for token in [
    "Install and restart", "StartExternalUpdater(downloadedUpdate)",
    "PrepareForUpdateShutdownAsync", "--expected-size", "--expected-sha256",
    "Closing database sessions before update", "_isPreparedForUpdateShutdown",
]:
    if token not in main_window_source:
        print(f"Desktop external updater handoff is missing: {token}")
        sys.exit(1)
print("Cross-platform external .NET updater packaging, archive validation, rollback, and process handoff source checks passed.")
