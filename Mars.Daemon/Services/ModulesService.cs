using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using Mars.Shared.Models;

namespace Mars.Daemon.Services;

/// <summary>
/// Manages the installation, listing, and deletion of MARS server modules.
/// </summary>
public class ModulesService
{
    private readonly DaemonConfigService _config;

    public ModulesService(DaemonConfigService config)
    {
        _config = config;
    }

    /// <summary>
    /// Gets a list of all installed modules, including their status (enabled/disabled).
    /// </summary>
    public List<ModuleInfo> GetModules()
    {
        var modules = new List<ModuleInfo>();
        var modulesDir = _config.ModulesDirectory;

        if (!Directory.Exists(modulesDir))
            return modules;

        var disabledModules = GetDisabledModules();

        var directories = Directory.GetDirectories(modulesDir);
        foreach (var dir in directories)
        {
            var manifestPath = Path.Combine(dir, "manifest.json");
            if (!File.Exists(manifestPath)) continue;

            try
            {
                var manifestContent = File.ReadAllText(manifestPath);
                var moduleInfo = JsonSerializer.Deserialize<ModuleInfo>(manifestContent);
                
                if (moduleInfo != null)
                {
                    moduleInfo.PackageName = Path.GetFileName(dir);
                    moduleInfo.IsDisabled = disabledModules.Contains(moduleInfo.PackageName);
                    modules.Add(moduleInfo);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ModulesService] Failed to read manifest for {dir}: {ex.Message}");
            }
        }

        return modules;
    }

    /// <summary>
    /// Deletes a module from the disk.
    /// </summary>
    public bool DeleteModule(string packageName)
    {
        if (string.IsNullOrWhiteSpace(packageName) || packageName.Contains("..") || packageName.Contains("/") || packageName.Contains("\\"))
        {
            Console.WriteLine("[ModulesService] Invalid package name for deletion.");
            return false;
        }

        var modulePath = Path.Combine(_config.ModulesDirectory, packageName);

        if (!Directory.Exists(modulePath))
        {
            Console.WriteLine($"[ModulesService] Module {packageName} does not exist.");
            return false;
        }

        try
        {
            Directory.Delete(modulePath, recursive: true);
            Console.WriteLine($"[ModulesService] Successfully deleted module: {packageName}");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ModulesService] Failed to delete module {packageName}: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Installs modules by downloading the marketplace repository archive, extracting it, and copying the required packages.
    /// </summary>
    public async Task<bool> InstallModulesAsync(IEnumerable<string> packageNames)
    {
        var packagesToInstall = packageNames?.ToList() ?? new List<string>();
        if (!packagesToInstall.Any())
            return true;

        const string repoZipUrl = "https://github.com/TheVekT/mars-hub/archive/refs/heads/main.zip";
        var tempZipPath = Path.GetTempFileName();
        var tempExtractPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

        bool allSuccess = true;

        try
        {
            Console.WriteLine($"[ModulesService] Downloading marketplace archive from {repoZipUrl}");
            using (var httpClient = new HttpClient())
            {
                var zipBytes = await httpClient.GetByteArrayAsync(repoZipUrl);
                await File.WriteAllBytesAsync(tempZipPath, zipBytes);
            }

            Console.WriteLine($"[ModulesService] Extracting archive to {tempExtractPath}");
            ZipFile.ExtractToDirectory(tempZipPath, tempExtractPath);

            var extractedModulesDir = Path.Combine(tempExtractPath, "mars-hub-main", "modules");
            if (!Directory.Exists(extractedModulesDir))
            {
                Console.WriteLine("[ModulesService] Expected modules directory not found in the extracted archive.");
                return false;
            }

            foreach (var packageName in packagesToInstall)
            {
                if (string.IsNullOrWhiteSpace(packageName) || packageName.Contains("..") || packageName.Contains("/") || packageName.Contains("\\"))
                {
                    Console.WriteLine($"[ModulesService] Invalid package name: {packageName}");
                    allSuccess = false;
                    continue;
                }

                var sourceModuleDir = Path.Combine(extractedModulesDir, packageName);
                if (!Directory.Exists(sourceModuleDir))
                {
                    Console.WriteLine($"[ModulesService] Module '{packageName}' not found in the marketplace archive.");
                    allSuccess = false;
                    continue;
                }

                var targetModuleDir = Path.Combine(_config.ModulesDirectory, packageName);

                try
                {
                    if (Directory.Exists(targetModuleDir))
                    {
                        Console.WriteLine($"[ModulesService] Removing existing module '{packageName}' before update.");
                        Directory.Delete(targetModuleDir, recursive: true);
                    }

                    if (!Directory.Exists(_config.ModulesDirectory))
                    {
                        Directory.CreateDirectory(_config.ModulesDirectory);
                    }

                    Console.WriteLine($"[ModulesService] Installing module '{packageName}'.");
                    CopyDirectory(sourceModuleDir, targetModuleDir);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ModulesService] Failed to install module '{packageName}': {ex.Message}");
                    allSuccess = false;
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ModulesService] Error during module installation process: {ex.Message}");
            allSuccess = false;
        }
        finally
        {
            try
            {
                if (File.Exists(tempZipPath)) File.Delete(tempZipPath);
                if (Directory.Exists(tempExtractPath)) Directory.Delete(tempExtractPath, recursive: true);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ModulesService] Warning: Failed to clean up temporary files: {ex.Message}");
            }
        }

        return allSuccess;
    }

    private void CopyDirectory(string sourceDir, string destDir)
    {
        Directory.CreateDirectory(destDir);

        foreach (var file in Directory.GetFiles(sourceDir))
        {
            File.Copy(file, Path.Combine(destDir, Path.GetFileName(file)), true);
        }

        foreach (var directory in Directory.GetDirectories(sourceDir))
        {
            CopyDirectory(directory, Path.Combine(destDir, Path.GetFileName(directory)));
        }
    }

    /// <summary>
    /// Checks all installed modules and disables those that are not compatible with the current system.
    /// </summary>
    public void DisableIncompatibleModules()
    {
        var allModules = GetModules();
        var currentPlatform = GetCurrentPlatformName();

        foreach (var module in allModules)
        {
            if (module.Compatibility == null || !module.Compatibility.Any())
                continue;

            // Check if current platform is in the compatibility list
            bool isCompatible = module.Compatibility.Any(p => p.Equals(currentPlatform, StringComparison.OrdinalIgnoreCase) || p.Equals("all", StringComparison.OrdinalIgnoreCase));
            
            if (!isCompatible && !module.IsDisabled)
            {
                Console.WriteLine($"[ModulesService] Module '{module.PackageName}' is incompatible with {currentPlatform}. Automatically disabling it.");
                module.IsDisabled = true;
                SetModuleState(module);
            }
        }
    }

    private string GetCurrentPlatformName()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return "windows";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) return "linux";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) return "macos";
        return "unknown";
    }

    /// <summary>
    /// Reads the server configuration file and returns a list of disabled module package names.
    /// </summary>
    private HashSet<string> GetDisabledModules()
    {
        var disabledModules = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var modulesStr = _config.GetPythonArg("--disabled-modules", string.Empty);
            if (!string.IsNullOrWhiteSpace(modulesStr))
            {
                var modules = modulesStr.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                foreach (var m in modules)
                {
                    disabledModules.Add(m.Trim());
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ModulesService] Failed to read disabled modules: {ex.Message}");
        }

        return disabledModules;
    }


    /// <summary>
    /// Updates the server configuration to enable or disable a module based on the provided ModuleInfo object.
    /// </summary>
    public bool SetModuleState(ModuleInfo moduleInfo)
    {
        if (moduleInfo == null || string.IsNullOrWhiteSpace(moduleInfo.PackageName))
            return false;

        var configPath = _config.ConfigPath;
        var disabledModules = GetDisabledModules();
        var isCurrentlyDisabled = disabledModules.Contains(moduleInfo.PackageName);

        if (moduleInfo.IsDisabled == isCurrentlyDisabled)
            return true; // No change needed

        if (moduleInfo.IsDisabled)
        {
            disabledModules.Add(moduleInfo.PackageName);
        }
        else
        {
            disabledModules.Remove(moduleInfo.PackageName);
        }

        try
        {
            bool success;
            if (disabledModules.Count > 0)
            {
                var modulesStr = string.Join(" ", disabledModules);
                success = _config.SetPythonArg("--disabled-modules", modulesStr);
            }
            else
            {
                success = _config.RemovePythonArg("--disabled-modules");
            }

            if (success)
            {
                Console.WriteLine($"[ModulesService] Module {moduleInfo.PackageName} state changed. Disabled: {moduleInfo.IsDisabled}");
            }
            return success;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ModulesService] Failed to update module state: {ex.Message}");
            return false;
        }
    }
}
