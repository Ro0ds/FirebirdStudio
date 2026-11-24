using Avalonia.Controls;
using Avalonia.Input;
using FirebirdStudio.Desktop.Services;

namespace FirebirdStudio.Desktop.Views;

internal sealed partial class SettingsWindow : Window
{
    public SettingsWindow(Key currentShortcut)
    {
        InitializeComponent();
        ExecuteSqlShortcutBox.ItemsSource = SqlExecutionShortcuts.Options;
        ExecuteSqlShortcutBox.SelectedItem = currentShortcut;
        ResetShortcutButton.Click += (_, _) => ExecuteSqlShortcutBox.SelectedItem = SqlExecutionShortcuts.Default;
        CancelSettingsButton.Click += (_, _) => Close();
        SaveSettingsButton.Click += (_, _) =>
        {
            if (ExecuteSqlShortcutBox.SelectedItem is Key shortcut)
                Close(shortcut);
        };
    }
}
