using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Threading;

namespace Mars.UI.Views;

public partial class ServerPage : UserControl
{
    public ServerPage()
    {
        InitializeComponent();

        var logsTextBlock = this.FindControl<TextBlock>("LogsTextBlock");
        var logsScrollViewer = this.FindControl<ScrollViewer>("LogsScrollViewer");

        if (logsTextBlock != null && logsScrollViewer != null)
        {
            logsTextBlock.PropertyChanged += (s, e) =>
            {
                if (e.Property.Name == nameof(TextBlock.Text))
                {
                    Dispatcher.UIThread.Post(() => { logsScrollViewer.ScrollToEnd(); }, DispatcherPriority.Background);
                }
            };
        }
    }

    protected override void OnDataContextChanged(System.EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is ViewModels.ServerViewModel vm)
        {
            vm.ShowMarketplaceDialogAsync = async (availableUpdates) =>
            {
                var window = new Windows.MarketplaceWindow
                {
                    DataContext = new ViewModels.MarketplaceWindowViewModel(availableUpdates)
                };
                
                var topLevel = TopLevel.GetTopLevel(this);
                if (topLevel is Window mainWindow)
                {
                    return await window.ShowDialog<IEnumerable<string>>(mainWindow);
                }
                return new List<string>();
            };

            vm.ShowConfirmDialogAsync = async (message) =>
            {
                var topLevel = TopLevel.GetTopLevel(this);
                if (topLevel is Window mainWindow)
                {
                    return await Windows.ConfirmDialog.Show(mainWindow, message);
                }
                return false;
            };
        }
    }
}