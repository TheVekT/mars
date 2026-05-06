using System;
using System.Collections.Generic;
using System.IO.Pipes;
using System.Text.Json;
using System.Threading.Tasks;
using Mars.Shared.Models;

namespace Mars.UI.Services;

public interface IIpcClientService
{
    Task<DaemonStatus?> GetStatusAsync();
    Task<DaemonStatus?> StartServerAsync();
    Task<DaemonStatus?> StopServerAsync();
    Task<string> DumpSchemaAsync();
    
    Task<List<ModuleInfo>> GetModulesAsync();
    Task<bool> SetModuleStateAsync(ModuleInfo module);
    Task<bool> DeleteModuleAsync(string packageName);
    Task<bool> InstallModuleAsync(IEnumerable<string> packageNames);

    Task<string> GetHostAsync();
    Task<string> GetPortAsync();
    Task<string> GetLogPathAsync();
    Task<bool> GetAutostartAsync();
    Task<bool> SetAutostartAsync(bool enabled);
    Task<bool> SetPasswordAsync(string password);
    Task<bool> SetHostAsync(string host);
    Task<bool> SetPortAsync(string port);
}

public class IpcClientService : IIpcClientService
{
    private const string PipeName = "MarsDaemonPipe";
    private const int TimeoutMs = 3000;

    private async Task<IpcResponse?> SendCommandAsync(string command, string payload = "")
    {
        try
        {
            var request = new IpcRequest 
            { 
                Command = command,
                Payload = payload
            };

            await using var pipeClient = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            await pipeClient.ConnectAsync(TimeoutMs);

            await using var writer = new System.IO.StreamWriter(pipeClient, leaveOpen: true) { AutoFlush = true };
            using var reader = new System.IO.StreamReader(pipeClient, leaveOpen: true);
            
            var requestJson = JsonSerializer.Serialize(request);
            await writer.WriteLineAsync(requestJson);
            
            var responseJson = await reader.ReadLineAsync();
            if (string.IsNullOrWhiteSpace(responseJson)) return null;

            return JsonSerializer.Deserialize<IpcResponse>(responseJson);
        }
        catch (TimeoutException)
        {
            Console.WriteLine("[IpcClientService] Connection to daemon timed out.");
            return null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[IpcClientService] Error sending command '{command}': {ex.Message}");
            return null;
        }
    }

    public async Task<DaemonStatus?> GetStatusAsync()
    {
        var response = await SendCommandAsync("STATUS");
        if (response != null && response.IsSuccess && !string.IsNullOrWhiteSpace(response.Data))
        {
            try { return JsonSerializer.Deserialize<DaemonStatus>(response.Data); } catch { }
        }
        return null;
    }

    public async Task<DaemonStatus?> StartServerAsync()
    {
        var response = await SendCommandAsync("START");
        if (response != null && response.IsSuccess && !string.IsNullOrWhiteSpace(response.Data))
        {
            try { return JsonSerializer.Deserialize<DaemonStatus>(response.Data); } catch { }
        }
        return null;
    }

    public async Task<DaemonStatus?> StopServerAsync()
    {
        var response = await SendCommandAsync("STOP");
        if (response != null && response.IsSuccess && !string.IsNullOrWhiteSpace(response.Data))
        {
            try { return JsonSerializer.Deserialize<DaemonStatus>(response.Data); } catch { }
        }
        return null;
    }

    public async Task<string> DumpSchemaAsync()
    {
        var response = await SendCommandAsync("DUMP-SCHEMA");
        return response != null && response.IsSuccess ? response.Data : string.Empty;
    }

    public async Task<List<ModuleInfo>> GetModulesAsync()
    {
        var response = await SendCommandAsync("GET-MODULES");
        if (response != null && response.IsSuccess && !string.IsNullOrWhiteSpace(response.Data))
        {
            try { return JsonSerializer.Deserialize<List<ModuleInfo>>(response.Data) ?? new List<ModuleInfo>(); } catch { }
        }
        return new List<ModuleInfo>();
    }

    public async Task<bool> SetModuleStateAsync(ModuleInfo module)
    {
        var payload = JsonSerializer.Serialize(module);
        var response = await SendCommandAsync("SET-MODULE-STATE", payload);
        return response != null && response.IsSuccess;
    }

    public async Task<bool> DeleteModuleAsync(string packageName)
    {
        var response = await SendCommandAsync("DELETE-MODULE", packageName);
        return response != null && response.IsSuccess;
    }

    public async Task<bool> InstallModuleAsync(IEnumerable<string> packageNames)
    {
        var payload = JsonSerializer.Serialize(packageNames);
        var response = await SendCommandAsync("INSTALL-MODULE", payload);
        return response != null && response.IsSuccess;
    }

    public async Task<string> GetHostAsync()
    {
        var response = await SendCommandAsync("GET-HOST");
        return response != null && response.IsSuccess ? response.Data : string.Empty;
    }

    public async Task<string> GetPortAsync()
    {
        var response = await SendCommandAsync("GET-PORT");
        return response != null && response.IsSuccess ? response.Data : string.Empty;
    }

    public async Task<string> GetLogPathAsync()
    {
        var response = await SendCommandAsync("GET-LOG-PATH");
        return response != null && response.IsSuccess ? response.Data : string.Empty;
    }

    public async Task<bool> GetAutostartAsync()
    {
        var response = await SendCommandAsync("GET-AUTOSTART");
        return response != null && response.IsSuccess &&
               response.Data.Equals("true", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<bool> SetAutostartAsync(bool enabled)
    {
        var response = await SendCommandAsync("SET-AUTOSTART", enabled ? "true" : "false");
        return response != null && response.IsSuccess;
    }

    public async Task<bool> SetPasswordAsync(string password)
    {
        var response = await SendCommandAsync("SET-PASSWORD", password);
        return response != null && response.IsSuccess;
    }

    public async Task<bool> SetHostAsync(string host)
    {
        var response = await SendCommandAsync("SET-HOST", host);
        return response != null && response.IsSuccess;
    }

    public async Task<bool> SetPortAsync(string port)
    {
        var response = await SendCommandAsync("SET-PORT", port);
        return response != null && response.IsSuccess;
    }
}
