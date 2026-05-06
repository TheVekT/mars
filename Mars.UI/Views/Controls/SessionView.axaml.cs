using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Mars.Shared.Models;
using Mars.UI.Models;
using Mars.UI.ViewModels;

namespace Mars.UI.Views.Controls;

public partial class SessionView : UserControl
{
    public SessionView()
    {
        InitializeComponent();
    }
    
    #region Attached Properties

    public static readonly AttachedProperty<SessionViewModel?> SessionModelProperty =
        AvaloniaProperty.RegisterAttached<SessionView, Control, SessionViewModel?>("SessionModel");

    public static void SetSessionModel(Control element, SessionViewModel? value) => element.SetValue(SessionModelProperty, value);
    public static SessionViewModel? GetSessionModel(Control element) => element.GetValue(SessionModelProperty);

    public static readonly AttachedProperty<ICommand?> ChangeFavouriteCommandProperty =
        AvaloniaProperty.RegisterAttached<SessionView, Control, ICommand?>("ChangeFavouriteCommand");

    public static void SetChangeFavoriteCommand(Control element, ICommand? value) => element.SetValue(ChangeFavouriteCommandProperty, value);
    public static ICommand? GetChangeFavoriteCommand(Control element) => element.GetValue(ChangeFavouriteCommandProperty);
    
    public static readonly AttachedProperty<ICommand?> ConnectCommandProperty =
        AvaloniaProperty.RegisterAttached<SessionView, Control, ICommand?>("ConnectCommand");
    
    public static void SetConnectCommand(Control element, ICommand? value) => element.SetValue(ConnectCommandProperty, value);
    public static ICommand? GetConnectCommand(Control element) => element.GetValue(ConnectCommandProperty);

    #endregion
}