using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Mars.UI.Views.Windows;

public partial class MessageDialog : Window
{
    public MessageDialog()
    {
        InitializeComponent();
    }

    public MessageDialog(string title, string message) : this()
    {
        Title = title;
        var titleBlock = this.FindControl<TextBlock>("TitleTextBlock");
        if (titleBlock != null) titleBlock.Text = title;
        
        var messageBlock = this.FindControl<TextBlock>("MessageTextBlock");
        if (messageBlock != null) messageBlock.Text = message;
    }

    private void OkButton_Click(object? sender, RoutedEventArgs e) => Close();
}
