using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using FirebirdStudio.Desktop.Services;

namespace FirebirdStudio.Desktop.Views;

public sealed partial class QueryHistoryView : UserControl
{
    internal event Action<QueryHistoryEntry>? RestoreRequested;
    internal event EventHandler? ClearRequested;
    internal event EventHandler? CollapseRequested;

    public QueryHistoryView()
    {
        InitializeComponent();
        HistoryEntries.SelectionChanged += (_, _) =>
        {
            var entry = SelectedEntry;
            HistoryPreviewSql.Text = entry?.Sql ?? string.Empty;
            HistoryPreview.IsVisible = entry is not null;
        };
        HistoryEntries.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && e.KeyModifiers == KeyModifiers.None && SelectedEntry is { } entry)
            {
                e.Handled = true;
                RestoreRequested?.Invoke(entry);
            }
        };
        HistoryEntries.DoubleTapped += (_, e) =>
        {
            var item = (e.Source as Control)?.FindAncestorOfType<ListBoxItem>(includeSelf: true);
            if (item?.DataContext is QueryHistoryEntry entry)
                RestoreRequested?.Invoke(entry);
        };
        RestoreHistoryButton.Click += (_, _) =>
        {
            if (SelectedEntry is { } entry)
                RestoreRequested?.Invoke(entry);
        };
        ClearHistoryButton.Click += (_, _) => ClearRequested?.Invoke(this, EventArgs.Empty);
        CollapseHistoryButton.Click += (_, _) => CollapseRequested?.Invoke(this, EventArgs.Empty);
    }

    internal QueryHistoryEntry? SelectedEntry
    {
        get => HistoryEntries.SelectedItem as QueryHistoryEntry;
        set => HistoryEntries.SelectedItem = value;
    }

    internal void BindEntries(IEnumerable<QueryHistoryEntry> entries) => HistoryEntries.ItemsSource = entries;

    internal void ShowDockControls()
    {
        HistoryTitle.IsVisible = true;
        CollapseHistoryButton.IsVisible = true;
    }

    internal void FocusCollapseAction() => CollapseHistoryButton.Focus();
}
