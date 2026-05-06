using System.Text.Json.Serialization;

namespace Mars.Shared.Models;

/// <summary>
/// Represents a module (plugin) installed in the MARS server.
/// </summary>
public class ModuleInfo
{
    /// <summary>
    /// Gets or sets the internal package name (folder name).
    /// </summary>
    [JsonPropertyName("package_name")]
    public string PackageName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the display name of the module from manifest.json.
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the version of the module.
    /// </summary>
    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the author of the module.
    /// </summary>
    [JsonPropertyName("author")]
    public string Author { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the description of the module.
    /// </summary>
    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether the module is currently disabled in the server config.
    /// </summary>
    [JsonPropertyName("is_disabled")]
    public bool IsDisabled { get; set; }

    /// <summary>
    /// Gets or sets the list of supported platforms (e.g. "windows", "linux", "macos").
    /// </summary>
    [JsonPropertyName("compatibility")]
    public List<string> Compatibility { get; set; } = new();

    /// <summary>
    /// Gets or sets the list of required tools for the module.
    /// </summary>
    [JsonPropertyName("required_tools")]
    public List<string> RequiredTools { get; set; } = new();
}
