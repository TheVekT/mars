using System.Collections.ObjectModel;
using Mars.UI.Models;
using Mars.UI.Services;
using Mars.UI.Stores;

namespace Mars.UI.ViewModels;

public partial class SettingsViewModel: ViewModelBase
{

    public AppStore AppStore { get; }

    public SettingsViewModel( AppStore appStore)
    {
        AppStore = appStore;
    }
    

}