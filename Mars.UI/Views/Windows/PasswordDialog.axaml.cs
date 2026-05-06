using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Mars.UI.Views.Windows;

public partial class PasswordDialog : Window
{
    public string? Password { get; private set; }

    public PasswordDialog()
    {
        InitializeComponent();
    }
    
    public PasswordDialog(string message) : this()
    {
        MessageTextBlock.Text = message;
    }

    private void ConnectButton_Click(object? sender, RoutedEventArgs e)
    {
        Password = PasswordTextBox.Text ?? string.Empty;
        Close(Password);
    }

    private void CancelButton_Click(object? sender, RoutedEventArgs e)
    {
        Close(null);
    }
}
