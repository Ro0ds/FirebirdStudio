using Avalonia.Controls;
using Avalonia.Threading;
using FirebirdStudio.Desktop.Services;

namespace FirebirdStudio.Desktop.Views;

public sealed partial class MessagesView : UserControl
{
    internal event EventHandler? ClearRequested;
    internal event Action<string>? CopyDetailsRequested;

    public MessagesView()
    {
        InitializeComponent();
        MessageLogItems.SelectionChanged += (_, _) => UpdateDetails();
        ClearMessagesButton.Click += (_, _) => ClearRequested?.Invoke(this, EventArgs.Empty);
        CopyMessageDetailsButton.Click += (_, _) =>
        {
            if (MessageLogItems.SelectedItem is StudioMessageEntry entry)
                CopyDetailsRequested?.Invoke(entry.Details);
        };
        CloseMessageDetailsButton.Click += (_, _) =>
        {
            MessageLogItems.SelectedItem = null;
            MessageLogItems.Focus();
        };
    }

    internal TextBlock CurrentStatus => StatusText;

    internal void BindEntries(IEnumerable<StudioMessageEntry> entries) => MessageLogItems.ItemsSource = entries;

    internal void ShowLatest()
    {
        MessageLogItems.SelectedItem = null;
        Dispatcher.UIThread.Post(() =>
        {
            if (MessageLogItems.ItemCount > 0)
                MessageLogItems.ScrollIntoView(0);
        }, DispatcherPriority.Loaded);
    }

    private void UpdateDetails()
    {
        var entry = MessageLogItems.SelectedItem as StudioMessageEntry;
        MessageDetailsText.Text = entry?.Details ?? string.Empty;
        MessageDetailsText.CaretIndex = 0;
        MessageDetailsPane.IsVisible = entry is not null;
        MessageWorkspace.ColumnDefinitions[1].Width = entry is null ? GridLength.Auto : new GridLength(1.15, GridUnitType.Star);
    }
}
