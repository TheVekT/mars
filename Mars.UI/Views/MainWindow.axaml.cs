using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Mars.UI.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void SkipStartScreen(object? sender, RoutedEventArgs e)
    {
        StartScreen.IsVisible = false;
        MainScreenContainer.IsVisible = true;
    }
}