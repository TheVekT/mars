using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Mars.UI.Models;

namespace Mars.UI.ViewModels.HttpModules;

public partial class HttpModuleViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _moduleName = string.Empty;

    [ObservableProperty]
    private string _compatibilityLabel = string.Empty;

    public ObservableCollection<HttpActionViewModel> Actions { get; } = new();

    public HttpModuleViewModel(HttpModuleSchema schema)
    {
        ModuleName = schema.ModuleName;
        CompatibilityLabel = string.Join(", ", schema.Compatibility);
    }
}
