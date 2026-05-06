namespace Mars.Daemon.Services;

/// <summary>
/// Manages the daemon configuration file (server.conf).
/// Responsible for all path resolution and config read/write operations.
/// Supports a structured format with sections [Server.Params] and [Daemon.Config].
/// </summary>
public class DaemonConfigService
{
    private readonly string _basePath;

    private const string ServerSection = "[Server.Params]";
    private const string DaemonSection = "[Daemon.Config]";

    public DaemonConfigService()
    {
        _basePath = AppDomain.CurrentDomain.BaseDirectory;
    }

    // -------------------------------------------------------------------------
    // Path resolution
    // -------------------------------------------------------------------------

    /// <summary>Returns the directory where mars-server lives.</summary>
    public string ServerDirectory => Path.Combine(_basePath, "mars-server");

    /// <summary>Returns the path to the modules directory.</summary>
    public string ModulesDirectory => Path.Combine(ServerDirectory, "modules");

    /// <summary>Returns the absolute path to the server log file.</summary>
    public string LogPath => Path.Combine(ServerDirectory, "server.log");

    /// <summary>Returns the path to the daemon configuration file.</summary>
    public string ConfigPath
    {
        get
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MarsApp");
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            return Path.Combine(dir, "server.conf");
        }
    }

    // -------------------------------------------------------------------------
    // Config bootstrap
    // -------------------------------------------------------------------------

    /// <summary>Creates server.conf with default values if it does not exist.</summary>
    public void EnsureConfigExists()
    {
        var path = ConfigPath;
        if (File.Exists(path)) return;

        try
        {
            var defaults = $@"{ServerSection}
# Python server startup parameters
--host 0.0.0.0 --port 8000

{DaemonSection}
# MARS Daemon service configuration
autostart = false
# Automatically disable modules that are incompatible with the current OS
disable_incompatible_modules = true
                ";
            File.WriteAllText(path, defaults);
            Console.WriteLine($"[Config] Default configuration created: {path}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Config] Error creating config file: {ex.Message}");
        }
    }

    // -------------------------------------------------------------------------
    // Section-based logic
    // -------------------------------------------------------------------------

    /// <summary>Returns the raw string under [Server.Params].</summary>
    public string GetPythonArgs()
    {
        EnsureConfigExists();
        try
        {
            var lines = File.ReadAllLines(ConfigPath);
            bool inSection = false;
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (trimmed.Equals(ServerSection, StringComparison.OrdinalIgnoreCase))
                {
                    inSection = true;
                    continue;
                }
                if (trimmed.StartsWith("[") && inSection) break;
                if (inSection && !string.IsNullOrWhiteSpace(trimmed) && !trimmed.StartsWith("#"))
                {
                    return trimmed;
                }
            }
        }
        catch { }
        return string.Empty;
    }

    /// <summary>Gets a specific argument value from the Python server parameters section.</summary>
    public string GetPythonArg(string key, string defaultValue)
    {
        var args = GetPythonArgs().Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        for (int i = 0; i < args.Count; i++)
        {
            if (args[i].Equals(key, StringComparison.OrdinalIgnoreCase))
            {
                var values = new List<string>();
                for (int j = i + 1; j < args.Count; j++)
                {
                    if (args[j].StartsWith("--")) break;
                    values.Add(args[j]);
                }
                return values.Count > 0 ? string.Join(" ", values) : string.Empty;
            }
        }
        return defaultValue;
    }

    /// <summary>Updates or adds a Python server parameter with one or more values.</summary>
    public bool SetPythonArg(string key, string value)
    {
        EnsureConfigExists();
        try
        {
            var rawArgs = GetPythonArgs();
            var args = rawArgs.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();

            // Find and remove existing key and its values
            for (int i = 0; i < args.Count; i++)
            {
                if (args[i].Equals(key, StringComparison.OrdinalIgnoreCase))
                {
                    args.RemoveAt(i);
                    while (i < args.Count && !args[i].StartsWith("--"))
                        args.RemoveAt(i);
                    break;
                }
            }

            // Add new key and value(s)
            args.Add(key);
            if (!string.IsNullOrWhiteSpace(value))
            {
                var valueParts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                args.AddRange(valueParts);
            }

            UpdateSection(ServerSection, string.Join(" ", args));
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Config] Failed to set python arg {key}: {ex.Message}");
            return false;
        }
    }

    /// <summary>Removes a Python server parameter and all its associated values.</summary>
    public bool RemovePythonArg(string key)
    {
        EnsureConfigExists();
        try
        {
            var rawArgs = GetPythonArgs();
            var args = rawArgs.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();

            bool found = false;
            for (int i = 0; i < args.Count; i++)
            {
                if (args[i].Equals(key, StringComparison.OrdinalIgnoreCase))
                {
                    args.RemoveAt(i);
                    while (i < args.Count && !args[i].StartsWith("--"))
                        args.RemoveAt(i);
                    found = true;
                    break;
                }
            }

            if (found)
                UpdateSection(ServerSection, string.Join(" ", args));
            
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Config] Failed to remove python arg {key}: {ex.Message}");
            return false;
        }
    }

    /// <summary>Retrieves a daemon setting from the [Daemon.Config] section.</summary>
    public string GetDaemonSetting(string key, string defaultValue)
    {
        EnsureConfigExists();
        try
        {
            var lines = File.ReadAllLines(ConfigPath);
            bool inSection = false;
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (trimmed.Equals(DaemonSection, StringComparison.OrdinalIgnoreCase)) { inSection = true; continue; }
                if (trimmed.StartsWith("[") && inSection) break;
                if (inSection && trimmed.Contains('='))
                {
                    var parts = trimmed.Split('=', 2);
                    if (parts[0].Trim().Equals(key, StringComparison.OrdinalIgnoreCase))
                        return parts[1].Trim();
                }
            }
        }
        catch { }
        return defaultValue;
    }

    /// <summary>Updates or adds a daemon setting in the [Daemon.Config] section.</summary>
    public bool SetDaemonSetting(string key, string value)
    {
        EnsureConfigExists();
        try
        {
            var lines = File.ReadAllLines(ConfigPath).ToList();
            int sectionIndex = -1;
            int nextSectionIndex = -1;

            for (int i = 0; i < lines.Count; i++)
            {
                if (lines[i].Trim().Equals(DaemonSection, StringComparison.OrdinalIgnoreCase))
                    sectionIndex = i;
                else if (sectionIndex != -1 && lines[i].Trim().StartsWith("["))
                {
                    nextSectionIndex = i;
                    break;
                }
            }

            if (sectionIndex == -1)
            {
                lines.Add("");
                lines.Add(DaemonSection);
                lines.Add($"{key} = {value}");
            }
            else
            {
                int end = nextSectionIndex == -1 ? lines.Count : nextSectionIndex;
                bool found = false;
                for (int i = sectionIndex + 1; i < end; i++)
                {
                    if (lines[i].Trim().StartsWith($"{key}=", StringComparison.OrdinalIgnoreCase) ||
                        lines[i].Trim().StartsWith($"{key} =", StringComparison.OrdinalIgnoreCase))
                    {
                        lines[i] = $"{key} = {value}";
                        found = true;
                        break;
                    }
                }
                if (!found)
                    lines.Insert(end, $"{key} = {value}");
            }

            File.WriteAllLines(ConfigPath, lines);
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Config] Failed to set daemon setting {key}: {ex.Message}");
            return false;
        }
    }

    private void UpdateSection(string sectionName, string newContent)
    {
        var lines = File.ReadAllLines(ConfigPath).ToList();
        int sectionIndex = -1;
        int nextSectionIndex = -1;

        for (int i = 0; i < lines.Count; i++)
        {
            if (lines[i].Trim().Equals(sectionName, StringComparison.OrdinalIgnoreCase))
                sectionIndex = i;
            else if (sectionIndex != -1 && lines[i].Trim().StartsWith("["))
            {
                nextSectionIndex = i;
                break;
            }
        }

        if (sectionIndex == -1)
        {
            lines.Add("");
            lines.Add(sectionName);
            lines.Add(newContent);
        }
        else
        {
            int end = nextSectionIndex == -1 ? lines.Count : nextSectionIndex;
            // Remove everything between sectionIndex and end that isn't a comment or the header
            int removeStart = sectionIndex + 1;
            int removeCount = end - removeStart;
            
            // Actually, let's keep comments.
            // We want to replace the FIRST non-comment non-empty line with newContent, 
            // or add it if none exists.
            bool replaced = false;
            for (int i = removeStart; i < end; i++)
            {
                var trimmed = lines[i].Trim();
                if (!string.IsNullOrWhiteSpace(trimmed) && !trimmed.StartsWith("#"))
                {
                    lines[i] = newContent;
                    replaced = true;
                    // Remove any other non-comment lines in this section
                    for (int j = i + 1; j < end; j++)
                    {
                        if (!string.IsNullOrWhiteSpace(lines[j].Trim()) && !lines[j].Trim().StartsWith("#"))
                        {
                            lines.RemoveAt(j);
                            end--;
                            j--;
                        }
                    }
                    break;
                }
            }
            if (!replaced)
                lines.Insert(removeStart, newContent);
        }

        File.WriteAllLines(ConfigPath, lines);
    }

    /// <summary>Gets the autostart flag for the Python server.</summary>
    public bool GetAutostart() =>
        GetDaemonSetting("autostart", "true").Equals("true", StringComparison.OrdinalIgnoreCase);

    /// <summary>Sets the autostart flag for the Python server.</summary>
    public bool SetAutostart(bool enabled) =>
        SetDaemonSetting("autostart", enabled ? "true" : "false");

    /// <summary>Gets whether incompatible modules should be automatically disabled.</summary>
    public bool GetDisableIncompatibleModules() =>
        GetDaemonSetting("disable_incompatible_modules", "true").Equals("true", StringComparison.OrdinalIgnoreCase);

    /// <summary>Sets whether incompatible modules should be automatically disabled.</summary>
    public bool SetDisableIncompatibleModules(bool enabled) =>
        SetDaemonSetting("disable_incompatible_modules", enabled ? "true" : "false");
}
