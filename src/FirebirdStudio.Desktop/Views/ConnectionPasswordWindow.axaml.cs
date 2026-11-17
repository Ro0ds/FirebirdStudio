using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace FirebirdStudio.Desktop.Views;

public sealed partial class ConnectionPasswordWindow : Window
{
    public string? Password { get; private set; }
    public bool RememberPassword { get; private set; }

    public ConnectionPasswordWindow(
        string connectionName,
        string? savedPassword = null,
        string? credentialStoreNotice = null)
    {
        AvaloniaXamlLoader.Load(this);
        this.FindControl<TextBlock>("ConnectionNameText")!.Text = connectionName;

        var passwordInput = this.FindControl<TextBox>("PasswordInput")!;
        var rememberPassword = this.FindControl<CheckBox>("RememberPasswordCheckBox")!;
        var statusText = this.FindControl<TextBlock>("CredentialStoreStatusText")!;

        passwordInput.Text = savedPassword ?? string.Empty;
        rememberPassword.IsChecked = savedPassword is not null;
        statusText.Text = credentialStoreNotice ?? (savedPassword is null
            ? "Enter the database password. It will not be saved unless you select the option below."
            : "A saved password was loaded from the operating system credential store. You can replace it here.");

        this.FindControl<Button>("ConnectButton")!.Click += OnConnectClick;
        this.FindControl<Button>("CancelButton")!.Click += (_, _) => ClosePrompt();
        Closed += (_, _) => ClearPasswordInput();
    }

    private void OnConnectClick(object? sender, RoutedEventArgs e)
    {
        Password = this.FindControl<TextBox>("PasswordInput")!.Text ?? string.Empty;
        RememberPassword = this.FindControl<CheckBox>("RememberPasswordCheckBox")!.IsChecked == true;
        ClearPasswordInput();
        Close();
    }

    public void ClearCapturedPassword() => Password = null;

    private void ClosePrompt()
    {
        Password = null;
        RememberPassword = false;
        ClearPasswordInput();
        Close();
    }

    private void ClearPasswordInput()
    {
        var input = this.FindControl<TextBox>("PasswordInput");
        if (input is not null)
            input.Text = string.Empty;
    }
}
