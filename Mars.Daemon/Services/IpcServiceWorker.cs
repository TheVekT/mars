using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using Mars.Shared.Models;

namespace Mars.Daemon.Services;

public class IpcServerWorker : BackgroundService
{
    private readonly PythonManager _pythonManager;
    private readonly ModulesService _modulesService;
    private const string PipeName = "MarsDaemonPipe";

    public IpcServerWorker(PythonManager pythonManager, ModulesService modulesService)
    {
        _pythonManager = pythonManager;
        _modulesService = modulesService;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Auto-start the Python server if configured
        if (_pythonManager.GetAutostart())
        {
            Console.WriteLine("[Daemon] Autostart enabled — starting Python server.");
            _pythonManager.StartServer();
        }
        else
        {
            Console.WriteLine("[Daemon] Autostart disabled — Python server will not start automatically.");
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            await using var pipeServer = CreateNamedPipeServer();

            try
            {
                await pipeServer.WaitForConnectionAsync(stoppingToken);
                using var reader = new StreamReader(pipeServer, leaveOpen: true);
                await using var writer = new StreamWriter(pipeServer, leaveOpen: true) { AutoFlush = true };

                var command = await reader.ReadLineAsync(stoppingToken);
                var response = await ProcessCommandAsync(command);
                
                await writer.WriteLineAsync(response);
            }
            catch (OperationCanceledException) { break; }
            catch (IOException)
            {
                // Client disconnected before the response was fully written.
            }
            catch (Exception ex) { Console.WriteLine($"[IPC Error] {ex.Message}"); }
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        Console.WriteLine("[Daemon] Service is stopping. Shutting down Python server...");
        _pythonManager.StopServer();
        await base.StopAsync(cancellationToken);
    }

    /// <summary>
    /// Creates a named pipe server that allows regular users to connect.
    /// </summary>
    private NamedPipeServerStream CreateNamedPipeServer()
    {
        if (OperatingSystem.IsWindows())
        {
            var pipeSecurity = new PipeSecurity();
            var sid = new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null);
            pipeSecurity.AddAccessRule(new PipeAccessRule(sid, PipeAccessRights.ReadWrite, AccessControlType.Allow));

            return NamedPipeServerStreamAcl.Create(
                PipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 0, 0, pipeSecurity);
        }
        else
        {
            var pipe = new NamedPipeServerStream(PipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
            
            var unixSocketPath = $"/tmp/CoreFxPipe_{PipeName}";
            if (File.Exists(unixSocketPath))
            {
                File.SetUnixFileMode(unixSocketPath,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite |
                    UnixFileMode.GroupRead | UnixFileMode.GroupWrite |
                    UnixFileMode.OtherRead | UnixFileMode.OtherWrite);
            }
            
            return pipe;
        }
    }

    private async Task<string> ProcessCommandAsync(string? rawRequest)
    {
        if (string.IsNullOrWhiteSpace(rawRequest))
            return JsonSerializer.Serialize(new IpcResponse { IsSuccess = false, Message = "Empty request" });

        IpcRequest? request;
        try
        {
            request = JsonSerializer.Deserialize<IpcRequest>(rawRequest);
        }
        catch (JsonException ex)
        {
            return JsonSerializer.Serialize(new IpcResponse { IsSuccess = false, Message = $"Invalid JSON request: {ex.Message}" });
        }

        if (request == null || string.IsNullOrWhiteSpace(request.Command))
            return JsonSerializer.Serialize(new IpcResponse { IsSuccess = false, Message = "Invalid request or missing command" });

        IpcResponse response;
        try
        {
            switch (request.Command.ToUpper())
            {
                case "START":
                    response = new IpcResponse { IsSuccess = true, Data = ExecuteStart() };
                    break;
                case "STOP":
                    response = new IpcResponse { IsSuccess = true, Data = ExecuteStop() };
                    break;
                case "STATUS":
                    response = new IpcResponse { IsSuccess = true, Data = GetStatus() };
                    break;
                case "DUMP-SCHEMA":
                    response = new IpcResponse { IsSuccess = true, Data = _pythonManager.DumpSchema() };
                    break;
                case "GET-MODULES":
                    response = new IpcResponse { IsSuccess = true, Data = JsonSerializer.Serialize(_modulesService.GetModules()) };
                    break;
                case "DELETE-MODULE":
                    if (string.IsNullOrWhiteSpace(request.Payload))
                    {
                        response = new IpcResponse { IsSuccess = false, Message = "Missing payload (package name) for deletion" };
                    }
                    else
                    {
                        var success = _modulesService.DeleteModule(request.Payload);
                        response = new IpcResponse { IsSuccess = success, Message = success ? "Module deleted" : "Failed to delete module" };
                    }
                    break;
                case "INSTALL-MODULE":
                    if (string.IsNullOrWhiteSpace(request.Payload))
                    {
                        response = new IpcResponse { IsSuccess = false, Message = "Missing payload (JSON array of package names) for installation" };
                    }
                    else
                    {
                        var packageNames = JsonSerializer.Deserialize<List<string>>(request.Payload);
                        if (packageNames == null || packageNames.Count == 0)
                        {
                            response = new IpcResponse { IsSuccess = false, Message = "Invalid or empty package names list" };
                        }
                        else
                        {
                            var success = await _modulesService.InstallModulesAsync(packageNames);
                            response = new IpcResponse { IsSuccess = success, Message = success ? "Modules installation process finished" : "Failed to install one or more modules" };
                        }
                    }
                    break;
                case "SET-MODULE-STATE":
                    if (string.IsNullOrWhiteSpace(request.Payload))
                    {
                        response = new IpcResponse { IsSuccess = false, Message = "Missing payload (ModuleInfo JSON)" };
                    }
                    else
                    {
                        var moduleObj = JsonSerializer.Deserialize<ModuleInfo>(request.Payload);
                        if (moduleObj == null)
                        {
                            response = new IpcResponse { IsSuccess = false, Message = "Invalid ModuleInfo payload" };
                        }
                        else
                        {
                            var success = _modulesService.SetModuleState(moduleObj);
                            response = new IpcResponse { IsSuccess = success, Message = success ? "Module state updated" : "Failed to update module state" };
                        }
                    }
                    break;
                case "SET-HOST":
                    if (string.IsNullOrWhiteSpace(request.Payload))
                    {
                        response = new IpcResponse { IsSuccess = false, Message = "Missing payload (IP address) for host" };
                    }
                    else
                    {
                        var success = _pythonManager.UpdateServerConfig("--host", request.Payload);
                        response = new IpcResponse { IsSuccess = success, Message = success ? "Host updated" : "Failed to update host" };
                    }
                    break;
                case "SET-PORT":
                    if (string.IsNullOrWhiteSpace(request.Payload))
                    {
                        response = new IpcResponse { IsSuccess = false, Message = "Missing payload (Port) for port" };
                    }
                    else
                    {
                        var success = _pythonManager.UpdateServerConfig("--port", request.Payload);
                        response = new IpcResponse { IsSuccess = success, Message = success ? "Port updated" : "Failed to update port" };
                    }
                    break;
                case "GET-HOST":
                    response = new IpcResponse { IsSuccess = true, Data = _pythonManager.GetServerConfig("--host", "0.0.0.0") };
                    break;
                case "GET-PORT":
                    response = new IpcResponse { IsSuccess = true, Data = _pythonManager.GetServerConfig("--port", "8000") };
                    break;
                case "GET-LOG-PATH":
                    response = new IpcResponse { IsSuccess = true, Data = _pythonManager.GetLogPath() };
                    break;
                case "GET-AUTOSTART":
                    response = new IpcResponse { IsSuccess = true, Data = _pythonManager.GetAutostart().ToString().ToLower() };
                    break;
                case "SET-AUTOSTART":
                    if (string.IsNullOrWhiteSpace(request.Payload))
                    {
                        response = new IpcResponse { IsSuccess = false, Message = "Missing payload (true/false)" };
                    }
                    else
                    {
                        var value = request.Payload.Trim().Equals("true", StringComparison.OrdinalIgnoreCase);
                        var success = _pythonManager.SetAutostart(value);
                        response = new IpcResponse { IsSuccess = success, Message = success ? $"Autostart set to {value}" : "Failed to update autostart" };
                    }
                    break;
                case "SET-PASSWORD":
                    bool pwdSuccess;
                    if (string.IsNullOrWhiteSpace(request.Payload))
                    {
                        pwdSuccess = _pythonManager.RemoveServerConfig("--password");
                        response = new IpcResponse { IsSuccess = pwdSuccess, Message = pwdSuccess ? "Password removed" : "Failed to remove password" };
                    }
                    else
                    {
                        pwdSuccess = _pythonManager.UpdateServerConfig("--password", request.Payload);
                        response = new IpcResponse { IsSuccess = pwdSuccess, Message = pwdSuccess ? "Password updated" : "Failed to update password" };
                    }
                    break;
                default:
                    response = new IpcResponse { IsSuccess = false, Message = "Unknown command" };
                    break;
            }
        }
        catch (Exception ex)
        {
            response = new IpcResponse { IsSuccess = false, Message = $"Command execution failed: {ex.Message}" };
        }

        return JsonSerializer.Serialize(response);
    }

    private string ExecuteStart()
    {
        _pythonManager.StartServer();
        return GetStatus();
    }

    private string ExecuteStop()
    {
        _pythonManager.StopServer();
        return GetStatus();
    }

    private string GetStatus()
    {
        var statusObj = new
        {
            IsRunning = _pythonManager.IsRunning,
            State = _pythonManager.CurrentState,
            LastError = _pythonManager.LastError
        };
        return JsonSerializer.Serialize(statusObj);
    }
}