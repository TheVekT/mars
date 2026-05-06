using Mars.Shared.Models;

namespace Mars.UI.Models;

public enum MarketplaceModuleStatus
{
    Install,
    Update
}

public class MarketplaceModule
{
    public ModuleInfo Info { get; set; } = null!;
    public MarketplaceModuleStatus Status { get; set; }
}
