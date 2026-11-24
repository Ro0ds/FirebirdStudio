using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using FirebirdStudio.Application.Connections;

namespace FirebirdStudio.Desktop.Views;

public sealed partial class QueryResultsView : UserControl
{
    private enum Status { Ready, Running, Completed, Cancelled, Failed }
    private sealed record SortHeader(int ColumnIndex, string Title, Button Button, TextBlock Indicator);

    private static readonly IComparer<IComparable?> SortValueComparer = Comparer<IComparable?>.Create(CompareSortValues);
    private ResultTableRow[] _resultRows = [];
    private SortHeader? _activeSortHeader;
    private bool _sortDescending;

    internal event EventHandler? ClearRequested;
    internal event Action<string>? CopyRowRequested;

    public QueryResultsView()
    {
        InitializeComponent();
        ClearResultsButton.Click += (_, _) => ClearRequested?.Invoke(this, EventArgs.Empty);
        CopyResultRowButton.Click += (_, _) => CopySelectedRow();
        QueryResultsGrid.SelectionChanged += (_, _) =>
            CopyResultRowButton.IsEnabled = QueryResultsGrid.SelectedItem is ResultTableRow;
        QueryResultsGrid.ContainerPrepared += OnResultRowPrepared;
        QueryResultsGrid.KeyDown += OnGridKeyDown;
        Reset();
    }

    internal void Reset() => ShowStatus(Status.Ready, "Ready");

    internal void ShowRunning() => ShowStatus(Status.Running, "Running query…");

    internal void ShowCancelling() => ShowStatus(Status.Running, "Cancelling query…");

    internal void ShowCancelled() => ShowStatus(Status.Cancelled, "Execution cancelled");

    internal void ShowFailed() => ShowStatus(Status.Failed, "Execution failed");

    internal void ShowResult(SqlExecutionResult result)
    {
        var summary = "Statement completed";
        if (result.Columns.Count > 0)
        {
            _resultRows = ResultTable.Populate(QueryResultsGrid, result.Columns, result.Rows, result.SortValues);
            if (_resultRows.Length > 0)
                ConfigureSortHeaders(result.Columns);
            var verb = result.HasMoreRows ? "shown" : "returned";
            summary = $"{FormatRowCount(result.Rows.Count)} {verb}";
        }
        else if (result.RowsAffected >= 0)
        {
            summary = $"{FormatRowCount(result.RowsAffected)} affected";
        }

        ShowStatus(Status.Completed, summary, result.Elapsed, result.HasMoreRows);
    }

    private void ShowStatus(Status status, string summary, TimeSpan? elapsed = null, bool isPartial = false)
    {
        var showGrid = status == Status.Completed && QueryResultsGrid.Columns.Count > 0;
        if (!showGrid)
        {
            _resultRows = [];
            _activeSortHeader = null;
            QueryResultsGrid.ItemsSource = null;
            QueryResultsGrid.Columns.Clear();
        }

        QueryResultsGrid.IsVisible = showGrid;
        ResultsSummary.Text = summary;
        ResultsSummary.Classes.Set("error", status == Status.Failed);
        ResultsSummary.Classes.Set("information", status == Status.Cancelled);
        ResultsSummary.Classes.Set("success", status == Status.Completed);
        ResultsElapsed.Text = elapsed is { } time ? $"{time.TotalMilliseconds:N0} ms" : null;
        ResultsElapsed.IsVisible = elapsed.HasValue;
        PartialResults.IsVisible = isPartial;
        ResultsHint.IsVisible = status == Status.Ready;
        ResultsProgress.IsVisible = status == Status.Running;
        ClearResultsButton.IsEnabled = status is Status.Completed or Status.Cancelled or Status.Failed;
    }

    private void ConfigureSortHeaders(IReadOnlyList<string> titles)
    {
        _activeSortHeader = null;
        for (var index = 0; index < titles.Count; index++)
        {
            var title = new TextBlock
            {
                Text = titles[index],
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
                Classes = { "panel-title" }
            };
            var indicator = new TextBlock
            {
                Margin = new Thickness(6, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Classes = { "secondary" }
            };
            var content = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*,Auto"),
                Children = { title, indicator }
            };
            Grid.SetColumn(indicator, 1);
            var button = new Button { Content = content, Classes = { "result-column-sort" } };
            var header = new SortHeader(index, titles[index], button, indicator);
            UpdateSortHeader(header, descending: null);
            button.Click += (_, _) => SortByColumn(header);
            var column = QueryResultsGrid.Columns[index];
            column.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            column.Header = button;
        }
    }

    private void SortByColumn(SortHeader header)
    {
        var descending = ReferenceEquals(_activeSortHeader, header) && !_sortDescending;
        var selectedRow = QueryResultsGrid.SelectedItem;
        var sorted = descending
            ? _resultRows.OrderByDescending(row => row.GetSortValue(header.ColumnIndex), SortValueComparer)
            : _resultRows.OrderBy(row => row.GetSortValue(header.ColumnIndex), SortValueComparer);
        QueryResultsGrid.ItemsSource = sorted.ToArray();
        QueryResultsGrid.SelectedItem = selectedRow;

        if (_activeSortHeader is { } previousHeader)
            UpdateSortHeader(previousHeader, descending: null);
        _activeSortHeader = header;
        _sortDescending = descending;
        UpdateSortHeader(header, descending);
    }

    private static void UpdateSortHeader(SortHeader header, bool? descending)
    {
        header.Indicator.Text = descending switch { true => "↓", false => "↑", _ => null };
        header.Indicator.IsVisible = descending.HasValue;
        var sortAction = descending == false ? "Sort descending" : "Sort ascending";
        var description = descending switch
        {
            true => $"{header.Title}, sorted descending. {sortAction}.",
            false => $"{header.Title}, sorted ascending. {sortAction}.",
            _ => $"{header.Title}. {sortAction}."
        };
        AutomationProperties.SetName(header.Button, description);
        ToolTip.SetTip(header.Button, description);
    }

    private static int CompareSortValues(IComparable? left, IComparable? right)
    {
        if (left is null)
            return right is null ? 0 : -1;
        if (right is null)
            return 1;
        var leftType = left.GetType();
        var rightType = right.GetType();
        return leftType == rightType
            ? left.CompareTo(right)
            : string.CompareOrdinal(leftType.FullName, rightType.FullName);
    }

    private void OnResultRowPrepared(object? sender, ContainerPreparedEventArgs e)
    {
        if (e.Container is not TableViewRow { ContextMenu: null } row)
            return;

        var copy = new MenuItem { Header = "Copy row" };
        copy.Click += (_, _) =>
        {
            if (row.DataContext is ResultTableRow result)
                CopyRow(result);
        };
        var menu = new ContextMenu { Items = { copy } };
        menu.Opened += (_, _) =>
        {
            if (row.DataContext is ResultTableRow result)
                QueryResultsGrid.SelectedItem = result;
        };
        row.ContextMenu = menu;
    }

    private void OnGridKeyDown(object? sender, KeyEventArgs e)
    {
        if (TextBox.CopyGesture?.Matches(e) == true && QueryResultsGrid.SelectedItem is ResultTableRow)
        {
            e.Handled = true;
            CopySelectedRow();
        }
    }

    private void CopySelectedRow()
    {
        if (QueryResultsGrid.SelectedItem is ResultTableRow row)
            CopyRow(row);
    }

    private void CopyRow(ResultTableRow row) =>
        CopyRowRequested?.Invoke(string.Join('\t', row.Values.Select(EscapeClipboardValue)));

    private static string EscapeClipboardValue(string value)
    {
        if (value.AsSpan().IndexOfAny('\t', '\r', '\n') < 0 && !value.Contains('"'))
            return value;
        return $"\"{value.Replace("\"", "\"\"")}\"";
    }

    private static string FormatRowCount(int count) => count == 1 ? "1 row" : $"{count:N0} rows";
}
