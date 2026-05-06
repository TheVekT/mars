using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Mars.UI.Models;

public class HttpModuleSchema
{
    [JsonPropertyName("module_name")]
    public string ModuleName { get; set; } = string.Empty;

    [JsonPropertyName("base_route")]
    public string BaseRoute { get; set; } = string.Empty;

    [JsonPropertyName("compatibility")]
    public List<string> Compatibility { get; set; } = new();

    [JsonPropertyName("requires_tools")]
    public List<string> RequiresTools { get; set; } = new();

    [JsonPropertyName("actions")]
    public List<HttpActionSchema> Actions { get; set; } = new();
}

public class HttpActionSchema
{
    [JsonPropertyName("endpoint")]
    public string Endpoint { get; set; } = string.Empty;

    [JsonPropertyName("method")]
    public string Method { get; set; } = "GET";

    [JsonPropertyName("label")]
    public string Label { get; set; } = string.Empty;

    [JsonPropertyName("interaction_type")]
    public string InteractionType { get; set; } = "read";

    [JsonPropertyName("data_type")]
    public string DataType { get; set; } = "scalar";

    [JsonPropertyName("refresh_interval_ms")]
    public int RefreshIntervalMs { get; set; }

    [JsonPropertyName("parameters")]
    public Dictionary<string, object>? Parameters { get; set; }
}

public class SchemaResponse
{
    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("schema")]
    public List<HttpModuleSchema> Schema { get; set; } = new();
}

public class WsModuleSchema
{
    [JsonPropertyName("module_name")]
    public string ModuleName { get; set; } = string.Empty;

    [JsonPropertyName("prefix")]
    public string Prefix { get; set; } = string.Empty;

    [JsonPropertyName("compatibility")]
    public List<string> Compatibility { get; set; } = new();
}

public class WsSchemaResponse
{
    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("schema")]
    public List<WsModuleSchema> Schema { get; set; } = new();
}
