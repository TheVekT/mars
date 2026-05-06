using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Mars.UI.ViewModels;

namespace Mars.UI.Views.Windows;

public partial class OverwriteDialog : Window
{
    public OverwriteDialog()
    {
        InitializeComponent();
    }

    public static async Task<OverwriteResult> Show(Window owner, string fileName)
    {
        var dialog = new OverwriteDialog();
        var nameBlock = dialog.FindControl<TextBlock>("FileNameText");
        if (nameBlock != null) nameBlock.Text = $"\"{fileName}\"";
        return await dialog.ShowDialog<OverwriteResult>(owner);
    }

    private void Overwrite_Click(object? sender, RoutedEventArgs e)
    {
        var applyAll = this.FindControl<CheckBox>("ApplyToAllCheck");
        Close(applyAll?.IsChecked == true ? OverwriteResult.OverwriteAll : OverwriteResult.Overwrite);
    }

    private void Skip_Click(object? sender, RoutedEventArgs e) => Close(OverwriteResult.Skip);
    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close(OverwriteResult.Cancel);
}
