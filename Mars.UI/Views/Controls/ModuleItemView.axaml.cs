using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Mars.Shared.Models;

namespace Mars.UI.Views;

public partial class ModuleItemView : UserControl
{
    public ModuleItemView()
    {
        InitializeComponent();
    }

    #region Attached Properties

    public static readonly AttachedProperty<ModuleInfo?> ModuleProperty =
        AvaloniaProperty.RegisterAttached<ModuleItemView, Control, ModuleInfo?>("Module");

    public static void SetModule(Control element, ModuleInfo? value) => element.SetValue(ModuleProperty, value);
    public static ModuleInfo? GetModule(Control element) => element.GetValue(ModuleProperty);

    public static readonly AttachedProperty<ICommand?> EnableCommandProperty =
        AvaloniaProperty.RegisterAttached<ModuleItemView, Control, ICommand?>("EnableCommand");

    public static void SetEnableCommand(Control element, ICommand? value) => element.SetValue(EnableCommandProperty, value);
    public static ICommand? GetEnableCommand(Control element) => element.GetValue(EnableCommandProperty);

    public static readonly AttachedProperty<ICommand?> DisableCommandProperty =
        AvaloniaProperty.RegisterAttached<ModuleItemView, Control, ICommand?>("DisableCommand");

    public static void SetDisableCommand(Control element, ICommand? value) => element.SetValue(DisableCommandProperty, value);
    public static ICommand? GetDisableCommand(Control element) => element.GetValue(DisableCommandProperty);

    public static readonly AttachedProperty<ICommand?> DeleteCommandProperty =
        AvaloniaProperty.RegisterAttached<ModuleItemView, Control, ICommand?>("DeleteCommand");

    public static void SetDeleteCommand(Control element, ICommand? value) => element.SetValue(DeleteCommandProperty, value);
    public static ICommand? GetDeleteCommand(Control element) => element.GetValue(DeleteCommandProperty);

    public static readonly AttachedProperty<bool> ShowActionsProperty =
        AvaloniaProperty.RegisterAttached<ModuleItemView, Control, bool>("ShowActions", true);

    public static void SetShowActions(Control element, bool value) => element.SetValue(ShowActionsProperty, value);
    public static bool GetShowActions(Control element) => element.GetValue(ShowActionsProperty);

    #endregion
}
