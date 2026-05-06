using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Mars.UI.Views.Windows;

public partial class ConfirmDialog : Window
{
    public ConfirmDialog()
    {
        InitializeComponent();
    }

    public static async Task<bool> Show(Window owner, string message)
    {
        var dialog = new ConfirmDialog();
        var messageBlock = dialog.FindControl<TextBlock>("MessageTextBlock");
        if (messageBlock != null)
        {
            messageBlock.Text = message;
        }
        return await dialog.ShowDialog<bool>(owner);
    }

    private void YesButton_Click(object? sender, RoutedEventArgs e) => Close(true);
    private void NoButton_Click(object? sender, RoutedEventArgs e) => Close(false);
}
