using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mars.UI.Models;
using Mars.UI.Services;

namespace Mars.UI.ViewModels.HttpModules;

public abstract partial class HttpActionViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _label = string.Empty;

    public HttpActionSchema Schema { get; }
    protected readonly HttpPollingService PollingService;
    protected readonly IMarsApiClient ApiClient;
    protected readonly string Ip;
    protected readonly string Port;
    protected readonly string Token;
    protected readonly Func<bool> IsActiveFunc;

    protected HttpActionViewModel(HttpActionSchema schema, HttpPollingService pollingService, IMarsApiClient apiClient, string ip, string port, string token, Func<bool> isActiveFunc)
    {
        Schema = schema;
        Label = schema.Label;
        PollingService = pollingService;
        ApiClient = apiClient;
        Ip = ip;
        Port = port;
        Token = token;
        IsActiveFunc = isActiveFunc;
    }

    public virtual void Initialize()
    {
    }

    protected JsonElement? ExtractBoundValue(JsonElement? json, string key)
    {
        if (!json.HasValue) return null;
        var el = json.Value;

        if (el.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in el.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.Object)
                {
                    var props = item.EnumerateObject().ToList();
                    if (props.Count >= 2 && props[0].Value.ToString() == key)
                    {
                        return props[1].Value;
                    }
                }
            }
        }
        else if (el.ValueKind == JsonValueKind.Object)
        {
            if (el.TryGetProperty(key, out var val)) return val;
            if (el.TryGetProperty("value", out var val2)) return val2;
        }

        return null;
    }
}

public partial class ScalarActionViewModel : HttpActionViewModel
{
    [ObservableProperty]
    private string _value = LocalizationService.Instance["HttpModule.Loading"];

    [ObservableProperty]
    private bool _isError = false;

    public ScalarActionViewModel(HttpActionSchema schema, HttpPollingService pollingService, IMarsApiClient apiClient, string ip, string port, string token, Func<bool> isActiveFunc) 
        : base(schema, pollingService, apiClient, ip, port, token, isActiveFunc) { }

    public override void Initialize()
    {
        if (Schema.RefreshIntervalMs > 0)
        {
            PollingService.Subscribe(Schema.Endpoint, Schema.RefreshIntervalMs, IsActiveFunc, OnDataReceived);
        }
        else
        {
            Task.Run(async () =>
            {
                try
                {
                    var res = await ApiClient.FetchTextAsync(Ip, Port, Token, Schema.Endpoint);
                    JsonElement? json = null;
                    try { using var doc = JsonDocument.Parse(res); json = doc.RootElement.Clone(); } catch { }
                    Avalonia.Threading.Dispatcher.UIThread.Post(() => OnDataReceived(res, json));
                }
                catch
                {
                    Avalonia.Threading.Dispatcher.UIThread.Post(() => { Value = LocalizationService.Instance["HttpModule.Error"]; IsError = true; });
                }
            });
        }
    }

    private void OnDataReceived(string rawText, JsonElement? json)
    {
        if (string.IsNullOrEmpty(rawText) && json == null)
        {
            Value = LocalizationService.Instance["HttpModule.Error"];
            IsError = true;
            return;
        }

        IsError = false;
        if (json.HasValue && json.Value.ValueKind == JsonValueKind.Object)
        {
            if (json.Value.TryGetProperty("value", out var v)) Value = v.ToString();
            else if (json.Value.TryGetProperty("text", out var t)) Value = t.ToString();
            else Value = rawText;
        }
        else
        {
            Value = rawText;
        }
    }
}

public partial class MultilineActionViewModel : HttpActionViewModel
{
    [ObservableProperty]
    private string _textValue = LocalizationService.Instance["HttpModule.Loading"];

    public MultilineActionViewModel(HttpActionSchema schema, HttpPollingService pollingService, IMarsApiClient apiClient, string ip, string port, string token, Func<bool> isActiveFunc) 
        : base(schema, pollingService, apiClient, ip, port, token, isActiveFunc) { }

    public override void Initialize()
    {
        if (Schema.RefreshIntervalMs > 0)
        {
            PollingService.Subscribe(Schema.Endpoint, Schema.RefreshIntervalMs, IsActiveFunc, OnDataReceived);
        }
        else
        {
            Task.Run(async () =>
            {
                try
                {
                    var res = await ApiClient.FetchTextAsync(Ip, Port, Token, Schema.Endpoint);
                    JsonElement? json = null;
                    try { using var doc = JsonDocument.Parse(res); json = doc.RootElement.Clone(); } catch { }
                    Avalonia.Threading.Dispatcher.UIThread.Post(() => OnDataReceived(res, json));
                }
                catch
                {
                    Avalonia.Threading.Dispatcher.UIThread.Post(() => TextValue = LocalizationService.Instance["HttpModule.Error"]);
                }
            });
        }
    }

    private void OnDataReceived(string rawText, JsonElement? json)
    {
        if (string.IsNullOrEmpty(rawText) && json == null)
        {
            TextValue = LocalizationService.Instance["HttpModule.Error"];
            return;
        }

        string val = rawText;
        if (json.HasValue && json.Value.ValueKind == JsonValueKind.Object)
        {
            if (json.Value.TryGetProperty("value", out var v)) val = v.ToString();
            else if (json.Value.TryGetProperty("text", out var t)) val = t.ToString();
        }

        TextValue = val.Replace("\\n", "\n");
    }
}

public class DataRow
{
    public ObservableCollection<string> Cells { get; set; } = new();
}

public partial class DatasetActionViewModel : HttpActionViewModel
{
    [ObservableProperty]
    private ObservableCollection<string> _columns = new();

    [ObservableProperty]
    private ObservableCollection<DataRow> _rows = new();

    [ObservableProperty]
    private string _statusMessage = LocalizationService.Instance["HttpModule.Loading"];

    [ObservableProperty]
    private bool _hasData = false;

    public DatasetActionViewModel(HttpActionSchema schema, HttpPollingService pollingService, IMarsApiClient apiClient, string ip, string port, string token, Func<bool> isActiveFunc) 
        : base(schema, pollingService, apiClient, ip, port, token, isActiveFunc) { }

    public override void Initialize()
    {
        if (Schema.RefreshIntervalMs > 0)
        {
            PollingService.Subscribe(Schema.Endpoint, Schema.RefreshIntervalMs, IsActiveFunc, OnDataReceived);
        }
        else
        {
            Task.Run(async () =>
            {
                try
                {
                    var res = await ApiClient.FetchTextAsync(Ip, Port, Token, Schema.Endpoint);
                    JsonElement? json = null;
                    try { using var doc = JsonDocument.Parse(res); json = doc.RootElement.Clone(); } catch { }
                    Avalonia.Threading.Dispatcher.UIThread.Post(() => OnDataReceived(res, json));
                }
                catch
                {
                    Avalonia.Threading.Dispatcher.UIThread.Post(() => { StatusMessage = LocalizationService.Instance["HttpModule.NetworkError"]; HasData = false; });
                }
            });
        }
    }

    private void OnDataReceived(string rawText, JsonElement? json)
    {
        if (json == null || json.Value.ValueKind != JsonValueKind.Array)
        {
            StatusMessage = LocalizationService.Instance["HttpModule.ErrorOrNoData"];
            HasData = false;
            return;
        }

        var array = json.Value.EnumerateArray().ToList();
        if (array.Count == 0)
        {
            StatusMessage = LocalizationService.Instance["HttpModule.NoData"];
            HasData = false;
            return;
        }

        HasData = true;
        StatusMessage = string.Empty;

        var firstRow = array[0].EnumerateObject().ToList();
        var currentCols = firstRow.Select(p => p.Name).ToList();

        if (Columns.Count != currentCols.Count || !Columns.SequenceEqual(currentCols))
        {
            Columns.Clear();
            foreach (var col in currentCols) Columns.Add(col);
        }

        var newRows = new ObservableCollection<DataRow>();
        foreach (var item in array)
        {
            var row = new DataRow();
            foreach (var col in currentCols)
            {
                if (item.TryGetProperty(col, out var val))
                {
                    row.Cells.Add(val.ToString());
                }
                else
                {
                    row.Cells.Add(string.Empty);
                }
            }
            newRows.Add(row);
        }
        
        Rows = newRows;
    }
}

public partial class BooleanActionViewModel : HttpActionViewModel
{
    [ObservableProperty]
    private bool _isChecked;

    private bool _isUpdatingFromServer;

    public BooleanActionViewModel(HttpActionSchema schema, HttpPollingService pollingService, IMarsApiClient apiClient, string ip, string port, string token, Func<bool> isActiveFunc) 
        : base(schema, pollingService, apiClient, ip, port, token, isActiveFunc) { }

    public override void Initialize()
    {
        if (Schema.Parameters != null && 
            Schema.Parameters.TryGetValue("bind_source", out var bindSourceObj) && 
            Schema.Parameters.TryGetValue("bind_key", out var bindKeyObj))
        {
            string bindSource = bindSourceObj?.ToString() ?? "";
            string bindKey = bindKeyObj?.ToString() ?? "";

            if (!string.IsNullOrEmpty(bindSource) && !string.IsNullOrEmpty(bindKey))
            {
                PollingService.Subscribe(bindSource, 2000, IsActiveFunc, (raw, json) =>
                {
                    var val = ExtractBoundValue(json, bindKey);
                    if (val.HasValue)
                    {
                        var strVal = val.Value.ToString()?.ToLower()?.Trim();
                        _isUpdatingFromServer = true;
                        IsChecked = strVal == "true" || strVal == "1" || strVal == "enabled" || strVal == "on" || strVal == "yes";
                        _isUpdatingFromServer = false;
                    }
                });
            }
        }
    }

    partial void OnIsCheckedChanged(bool value)
    {
        if (_isUpdatingFromServer) return;
        Task.Run(async () =>
        {
            try
            {
                var payload = JsonSerializer.Serialize(new { state = value });
                await ApiClient.PostJsonAsync(Ip, Port, Token, Schema.Endpoint, payload);
            }
            catch { }
        });
    }
}

public partial class RangeActionViewModel : HttpActionViewModel
{
    [ObservableProperty]
    private double _minimum = 0;

    [ObservableProperty]
    private double _maximum = 100;

    [ObservableProperty]
    private double _value;

    [ObservableProperty]
    private string _valueText = "-";

    private bool _isUpdatingFromServer;
    private System.Threading.CancellationTokenSource? _debounceCts;

    public RangeActionViewModel(HttpActionSchema schema, HttpPollingService pollingService, IMarsApiClient apiClient, string ip, string port, string token, Func<bool> isActiveFunc) 
        : base(schema, pollingService, apiClient, ip, port, token, isActiveFunc) 
    {
        if (schema.Parameters != null)
        {
            if (schema.Parameters.TryGetValue("min_val", out var minObj) && double.TryParse(minObj?.ToString(), out var min)) Minimum = min;
            if (schema.Parameters.TryGetValue("max_val", out var maxObj) && double.TryParse(maxObj?.ToString(), out var max)) Maximum = max;
        }
    }

    public override void Initialize()
    {
        if (Schema.Parameters != null && 
            Schema.Parameters.TryGetValue("bind_source", out var bindSourceObj) && 
            Schema.Parameters.TryGetValue("bind_key", out var bindKeyObj))
        {
            string bindSource = bindSourceObj?.ToString() ?? "";
            string bindKey = bindKeyObj?.ToString() ?? "";

            if (!string.IsNullOrEmpty(bindSource) && !string.IsNullOrEmpty(bindKey))
            {
                PollingService.Subscribe(bindSource, 2000, IsActiveFunc, (raw, json) =>
                {
                    var val = ExtractBoundValue(json, bindKey);
                    if (val.HasValue && double.TryParse(val.Value.ToString(), out var parsedVal))
                    {
                        _isUpdatingFromServer = true;
                        Value = parsedVal;
                        ValueText = parsedVal.ToString();
                        _isUpdatingFromServer = false;
                    }
                });
            }
        }
    }

    partial void OnValueChanged(double value)
    {
        int intVal = (int)Math.Round(value);
        ValueText = intVal.ToString();
        if (_isUpdatingFromServer) return;

        _debounceCts?.Cancel();
        _debounceCts = new System.Threading.CancellationTokenSource();
        var token = _debounceCts.Token;

        Task.Run(async () =>
        {
            try
            {
                await Task.Delay(150, token);
                if (token.IsCancellationRequested) return;

                var payload = JsonSerializer.Serialize(new { value = intVal });
                await ApiClient.PostJsonAsync(Ip, Port, Token, Schema.Endpoint, payload);
            }
            catch { }
        }, token);
    }
}

public partial class ExecuteButtonActionViewModel : HttpActionViewModel
{
    [ObservableProperty]
    private bool _isDanger;

    public ExecuteButtonActionViewModel(HttpActionSchema schema, HttpPollingService pollingService, IMarsApiClient apiClient, string ip, string port, string token, Func<bool> isActiveFunc) 
        : base(schema, pollingService, apiClient, ip, port, token, isActiveFunc) 
    {
        if (schema.Parameters != null && schema.Parameters.TryGetValue("danger_level", out var d) && d?.ToString() == "high")
        {
            IsDanger = true;
        }
    }

    [RelayCommand]
    private async Task ExecuteAsync()
    {
        try
        {
            await ApiClient.PostJsonAsync(Ip, Port, Token, Schema.Endpoint, "{}");
        }
        catch { }
    }
}

public class FormOptionViewModel
{
    public string Display { get; set; } = string.Empty;
    public object? Value { get; set; }
}

public partial class FormParameterViewModel : ObservableObject
{
    public string Name { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    
    // For Select
    [ObservableProperty]
    private ObservableCollection<FormOptionViewModel> _options = new();
    
    [ObservableProperty]
    private FormOptionViewModel? _selectedOption;
    
    // For Input
    [ObservableProperty]
    private string _inputValue = string.Empty;

    public object? GetValue()
    {
        if (Type == "select_from_dataset") return SelectedOption?.Value;
        return InputValue;
    }
}

public partial class ExecuteFormActionViewModel : HttpActionViewModel
{
    [ObservableProperty]
    private bool _isDanger;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _isExecuting;

    public ObservableCollection<FormParameterViewModel> FormParameters { get; } = new();

    public ExecuteFormActionViewModel(HttpActionSchema schema, HttpPollingService pollingService, IMarsApiClient apiClient, string ip, string port, string token, Func<bool> isActiveFunc) 
        : base(schema, pollingService, apiClient, ip, port, token, isActiveFunc) 
    {
        if (schema.Parameters != null && schema.Parameters.TryGetValue("danger_level", out var d) && d?.ToString() == "high")
        {
            IsDanger = true;
        }
    }

    public override void Initialize()
    {
        if (Schema.Parameters != null && Schema.Parameters.TryGetValue("params_schema", out var schemaObj))
        {
            var jsonStr = schemaObj?.ToString();
            if (!string.IsNullOrEmpty(jsonStr))
            {
                try
                {
                    var paramsList = JsonSerializer.Deserialize<List<Dictionary<string, object>>>(jsonStr);
                    if (paramsList != null)
                    {
                        foreach (var param in paramsList)
                        {
                            var vm = new FormParameterViewModel
                            {
                                Name = param.GetValueOrDefault("name")?.ToString() ?? "",
                                Label = param.GetValueOrDefault("label")?.ToString() ?? "",
                                Type = param.GetValueOrDefault("type")?.ToString() ?? ""
                            };

                            if (vm.Type == "select_from_dataset")
                            {
                                string sourceEndpoint = param.GetValueOrDefault("source_endpoint")?.ToString() ?? "";
                                string valueKey = param.GetValueOrDefault("value_key")?.ToString() ?? "";
                                string displayKey = param.GetValueOrDefault("display_key")?.ToString() ?? "";

                                vm.Options.Add(new FormOptionViewModel { Display = LocalizationService.Instance["HttpModule.Loading"] });
                                
                                Task.Run(async () =>
                                {
                                    try
                                    {
                                        var res = await ApiClient.FetchTextAsync(Ip, Port, Token, sourceEndpoint);
                                        using var doc = JsonDocument.Parse(res);
                                        var json = doc.RootElement.Clone();
                                        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                                        {
                                            vm.Options.Clear();
                                            if (json.ValueKind == JsonValueKind.Array)
                                            {
                                                foreach (var item in json.EnumerateArray())
                                                {
                                                    string optVal = item.TryGetProperty(valueKey, out var vk) ? vk.ToString() : "";
                                                    string displayVal = item.TryGetProperty(displayKey, out var dk) ? dk.ToString() : "";
                                                    vm.Options.Add(new FormOptionViewModel { Display = $"{displayVal} (ID: {optVal})", Value = optVal });
                                                }
                                            }
                                        });
                                    }
                                    catch
                                    {
                                        Avalonia.Threading.Dispatcher.UIThread.Post(() => { vm.Options.Clear(); vm.Options.Add(new FormOptionViewModel { Display = LocalizationService.Instance["HttpModule.Error"] }); });
                                    }
                                });
                            }

                            FormParameters.Add(vm);
                        }
                    }
                }
                catch { }
            }
        }
    }

    [RelayCommand]
    private async Task ExecuteAsync()
    {
        if (IsExecuting) return;
        IsExecuting = true;
        StatusMessage = LocalizationService.Instance["HttpModule.Executing"];

        try
        {
            var payload = new Dictionary<string, object>();
            foreach (var p in FormParameters)
            {
                var val = p.GetValue();
                if (val != null) payload[p.Name] = val;
            }

            var jsonPayload = JsonSerializer.Serialize(payload);
            var res = await ApiClient.PostJsonAsync(Ip, Port, Token, Schema.Endpoint, jsonPayload);
            
            try 
            {
                using var doc = JsonDocument.Parse(res);
                var json = doc.RootElement;
                if (json.TryGetProperty("message", out var msg)) StatusMessage = msg.ToString();
                else StatusMessage = LocalizationService.Instance["HttpModule.Done"];
            }
            catch { StatusMessage = LocalizationService.Instance["HttpModule.Done"]; }
        }
        catch
        {
            StatusMessage = LocalizationService.Instance["HttpModule.NetworkError"];
        }
        finally
        {
            IsExecuting = false;
        }
    }
}
