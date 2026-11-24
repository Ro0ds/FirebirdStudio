using Avalonia.Controls;
using Avalonia.Input;
using FirebirdStudio.Application.Connections;

namespace FirebirdStudio.Desktop.Views;

public sealed partial class QueryResultsView : UserControl
{
    private enum Status { Ready, Running, Completed, Cancelled, Failed }

    internal event EventHandler? ClearRequested;
    internal event Action<string>? CopyRowRequested;

    public QueryResultsView()
    {
        InitializeComponent();
        ClearResultsButton.Click += (_, _) => ClearRequested?.Invoke(this, EventArgs.Empty);
        CopyResultRowButton.Click += (_, _) => CopySelectedRow();
        QueryResultsGrid.SelectionChanged += (_, _) =>
            CopyResultRowButton.IsEnabled = QueryResultsGrid.SelectedItem is ResultTableRow;
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
            ResultTable.Populate(QueryResultsGrid, result.Columns, result.Rows);
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
            CopyRowRequested?.Invoke(string.Join('\t', row.Values.Select(EscapeClipboardValue)));
    }

    private static string EscapeClipboardValue(string value)
    {
        if (value.AsSpan().IndexOfAny('\t', '\r', '\n') < 0 && !value.Contains('"'))
            return value;
        return $"\"{value.Replace("\"", "\"\"")}\"";
    }

    private static string FormatRowCount(int count) => count == 1 ? "1 row" : $"{count:N0} rows";
}
