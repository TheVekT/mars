using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Mars.Shared.Models;
using Mars.UI.Models;

namespace Mars.UI.Services;

public interface IMarketplaceService
{
    /// <summary>
    /// Fetches the list of registered modules from the MARS Hub marketplace.
    /// </summary>
    /// <returns>A list of registered modules.</returns>
    Task<List<ModuleInfo>> GetRegisteredModulesAsync();

    /// <summary>
    /// Compares installed modules against registered modules to find those that are not installed or have a newer version available.
    /// </summary>
    /// <param name="installedModules">The list of currently installed modules.</param>
    /// <param name="registeredModules">The list of modules registered in the marketplace.</param>
    /// <returns>A list of modules available for installation or update with their status.</returns>
    List<MarketplaceModule> GetAvailableUpdates(List<ModuleInfo> installedModules, List<ModuleInfo> registeredModules);
}

public class MarketplaceService : IMarketplaceService
{
    private const string MarketplaceUrl = "https://raw.githubusercontent.com/TheVekT/mars-hub/refs/heads/main/index.json";
    private readonly HttpClient _httpClient;

    public MarketplaceService()
    {
        _httpClient = new HttpClient();
    }

    public async Task<List<ModuleInfo>> GetRegisteredModulesAsync()
    {
        try
        {
            var json = await _httpClient.GetStringAsync(MarketplaceUrl);
            var modules = JsonSerializer.Deserialize<List<ModuleInfo>>(json);
            return modules ?? new List<ModuleInfo>();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MarketplaceService] Failed to fetch registered modules: {ex.Message}");
            return new List<ModuleInfo>();
        }
    }

    public List<MarketplaceModule> GetAvailableUpdates(List<ModuleInfo> installedModules, List<ModuleInfo> registeredModules)
    {
        var availableUpdates = new List<MarketplaceModule>();

        if (registeredModules == null || !registeredModules.Any())
        {
            return availableUpdates;
        }

        var installedDict = installedModules?
            .Where(m => !string.IsNullOrWhiteSpace(m.PackageName))
            .ToDictionary(m => m.PackageName, StringComparer.OrdinalIgnoreCase) ?? new Dictionary<string, ModuleInfo>();

        foreach (var registeredModule in registeredModules)
        {
            if (string.IsNullOrWhiteSpace(registeredModule.PackageName))
            {
                continue;
            }

            // Not installed yet
            if (!installedDict.TryGetValue(registeredModule.PackageName, out var installedModule))
            {
                availableUpdates.Add(new MarketplaceModule 
                { 
                    Info = registeredModule, 
                    Status = MarketplaceModuleStatus.Install 
                });
                continue;
            }

            // Installed, check for version update
            bool isUpdate = false;
            if (Version.TryParse(registeredModule.Version, out var registeredVersion) &&
                Version.TryParse(installedModule.Version, out var installedVersion))
            {
                if (registeredVersion > installedVersion)
                {
                    isUpdate = true;
                }
            }
            else
            {
                // Fallback string comparison
                if (!string.Equals(registeredModule.Version, installedModule.Version, StringComparison.OrdinalIgnoreCase))
                {
                    isUpdate = true;
                }
            }

            if (isUpdate)
            {
                availableUpdates.Add(new MarketplaceModule 
                { 
                    Info = registeredModule, 
                    Status = MarketplaceModuleStatus.Update 
                });
            }
        }

        return availableUpdates;
    }
}
