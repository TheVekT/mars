using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Mars.Shared.Models;
using Mars.UI.Helpers;
using Mars.UI.Models;

namespace Mars.UI.ViewModels;

public partial class SelectableModuleItem : ObservableObject
{
    [ObservableProperty]
    private bool _isSelected;

    public MarketplaceModule MarketplaceModule { get; }
    
    public ModuleInfo Module => MarketplaceModule.Info;

    public SelectableModuleItem(MarketplaceModule module)
    {
        MarketplaceModule = module;
    }
}

public partial class MarketplaceWindowViewModel : ViewModelBase
{
    public ObservableRangeCollection<SelectableModuleItem> Modules { get; } = new();

    public IEnumerable<SelectableModuleItem> Updates => Modules.Where(m => m.MarketplaceModule.Status == MarketplaceModuleStatus.Update);
    public IEnumerable<SelectableModuleItem> NewModules => Modules.Where(m => m.MarketplaceModule.Status == MarketplaceModuleStatus.Install);

    public int UpdatesCount => Updates.Count();
    public int NewModulesCount => NewModules.Count();
    
    public bool HasUpdates => UpdatesCount > 0;
    public bool HasNewModules => NewModulesCount > 0;

    public MarketplaceWindowViewModel(IEnumerable<MarketplaceModule> availableModules)
    {
        var items = availableModules.Select(m => new SelectableModuleItem(m)).ToList();
        Modules.ReplaceRange(items);
        
        foreach(var item in Modules)
        {
            item.PropertyChanged += (s, e) => 
            {
                if (e.PropertyName == nameof(SelectableModuleItem.IsSelected))
                {
                    OnPropertyChanged(nameof(SelectedCount));
                    OnPropertyChanged(nameof(CanInstall));
                }
            };
        }
    }

    public int SelectedCount => Modules.Count(m => m.IsSelected);
    public bool CanInstall => SelectedCount > 0;

    public IEnumerable<string> GetSelectedPackages()
    {
        return Modules.Where(m => m.IsSelected).Select(m => m.Module.PackageName).ToList();
    }
}
