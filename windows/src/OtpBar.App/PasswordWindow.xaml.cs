using System.Windows;

namespace OtpBar.App;

public partial class PasswordWindow : Window
{
    private PasswordWindow(string message)
    {
        InitializeComponent();
        MessageText.Text = message;
        Loaded += (_, _) => PasswordInput.Focus();
    }

    /// <summary>Returns null when the import was cancelled. An empty string is a valid 2FAS password.</summary>
    public static string? Ask(Window owner, string message)
    {
        var window = new PasswordWindow(message) { Owner = owner };
        return window.ShowDialog() == true ? window.PasswordInput.Password : null;
    }

    private void OnConfirm(object sender, RoutedEventArgs e) => DialogResult = true;

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}
