using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Mars.UI.Views;

public partial class ConnectionPage : UserControl
{
    public ConnectionPage()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(System.EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is ViewModels.ConnectionViewModel vm)
        {
            vm.ShowPasswordDialogAsync = async (message) =>
            {
                var window = new Windows.PasswordDialog(message);
                var topLevel = TopLevel.GetTopLevel(this);
                if (topLevel is Window mainWindow)
                {
                    return await window.ShowDialog<string?>(mainWindow);
                }
                return null;
            };

            vm.ShowMessageDialogAsync = async (title, message) =>
            {
                var window = new Windows.MessageDialog(title, message);
                var topLevel = TopLevel.GetTopLevel(this);
                if (topLevel is Window mainWindow)
                {
                    await window.ShowDialog(mainWindow);
                }
            };

            vm.OpenRemoteControlWindowAsync = async () =>
            {
                var window = new Windows.RemoteControlWindow { DataContext = vm };
                var topLevel = TopLevel.GetTopLevel(this);
                if (topLevel is Window mainWindow)
                {
                    await window.ShowDialog(mainWindow);
                }
                else
                {
                    window.Show();
                }
            };

            vm.OpenFileExplorerWindowAsync = async () =>
            {
                var window = new Windows.FileExplorerWindow { DataContext = vm };
                var topLevel = TopLevel.GetTopLevel(this);
                if (topLevel is Window mainWindow)
                {
                    await window.ShowDialog(mainWindow);
                }
                else
                {
                    window.Show();
                }
            };
        }
    }
}