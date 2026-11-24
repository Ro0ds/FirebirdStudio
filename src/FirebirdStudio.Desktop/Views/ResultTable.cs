using Avalonia.Controls;
using Avalonia.Data;

namespace FirebirdStudio.Desktop.Views;

internal sealed record ResultTableRow(string[] Values);

internal static class ResultTable
{
    public static void Populate(
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

        grid.ItemsSource = rows.Select(row => new ResultTableRow(row.ToArray())).ToArray();
    }
}
