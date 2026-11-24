using Avalonia.Controls;
using Avalonia.Data;

namespace FirebirdStudio.Desktop.Views;

internal sealed record ResultTableRow(string[] Values, IReadOnlyList<IComparable?>? SortValues)
{
    public IComparable? GetSortValue(int columnIndex) =>
        SortValues is { } values ? values[columnIndex] : Values[columnIndex];
}

internal static class ResultTable
{
    public static ResultTableRow[] Populate(
        TableView grid,
        IReadOnlyList<string> columns,
        IReadOnlyList<IReadOnlyList<string>> rows,
        IReadOnlyList<IReadOnlyList<IComparable?>>? sortValues = null)
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

        var items = rows.Select((row, index) => new ResultTableRow(row.ToArray(), sortValues?[index])).ToArray();
        grid.ItemsSource = items;
        return items;
    }
}
