using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Mars.UI.ViewModels;

namespace Mars.UI.Views.Windows;

public partial class MarketplaceWindow : Window
{
    public MarketplaceWindow()
    {
        InitializeComponent();
    }

    private void InstallButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MarketplaceWindowViewModel vm)
        {
            var packages = vm.GetSelectedPackages();
            Close(packages);
        }
        else
        {
            Close(new List<string>());
        }
    }
}
