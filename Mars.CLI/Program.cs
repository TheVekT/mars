using System.IO.Pipes;
using System.Text.Json;
using System.Text.Json.Nodes;
using Mars.Shared.Models;

namespace Mars.CLI;

internal class Program
{
    private const string PipeName = "MarsDaemonPipe";
    
    private const int TimeoutMs = 3000;

    static async Task Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        
        if (args.Length == 0)
        {
            ShowHelp();
            return;
        }

        var command = args[0].ToLower();
        
        switch (command)
        {
            case "--start":
                await ExecuteSimpleCommand("START");
                break;
            case "--stop":
                await ExecuteSimpleCommand("STOP");
                break;
            case "--status":
                await ExecuteSimpleCommand("STATUS");
                break;
            case "--schema":
                await ExecuteSimpleCommand("DUMP-SCHEMA");
                break;
            case "--modules":
                await ExecuteGetModules();
                break;
            case "--enable-module":
                if (args.Length < 2) Console.WriteLine("Missing package name.");
                else await ExecuteSetModuleState(args[1], false);
                break;
            case "--disable-module":
                if (args.Length < 2) Console.WriteLine("Missing package name.");
                else await ExecuteSetModuleState(args[1], true);
                break;
            case "--delete-module":
                if (args.Length < 2) Console.WriteLine("Missing package name.");
                else await ExecuteDeleteModule(args[1]);
                break;
            case "--set-host":
                if (args.Length < 2) Console.WriteLine("Missing IP address.");
                else await ExecuteSetConfig("SET-HOST", args[1]);
                break;
            case "--set-port":
                if (args.Length < 2) Console.WriteLine("Missing port number.");
                else await ExecuteSetConfig("SET-PORT", args[1]);
                break;
            case "--get-host":
                await ExecuteGetConfig("GET-HOST");
                break;
            case "--get-port":
                await ExecuteGetConfig("GET-PORT");
                break;
            case "--get-autostart":
                await ExecuteGetConfig("GET-AUTOSTART");
                break;
            case "--set-autostart":
                if (args.Length < 2) Console.WriteLine("Missing value (true/false).");
                else await ExecuteSetConfig("SET-AUTOSTART", args[1]);
                break;
            case "--set-password":
                // If only --set-password is provided without a second arg, assume empty string (remove password)
                var pwd = args.Length > 1 ? args[1] : string.Empty;
                await ExecuteSetConfig("SET-PASSWORD", pwd);
                break;
            case "--install-module":
                if (args.Length < 2) Console.WriteLine("Missing module package name(s).");
                else await ExecuteInstallModule(args.Skip(1).ToList());
                break;
            default:
                Console.WriteLine($"[Error] Unknown command: {command}");
                ShowHelp();
                break;
        }
    }

    private static async Task ExecuteSimpleCommand(string ipcCommand)
    {
        try
        {
            var request = new IpcRequest { Command = ipcCommand };
            var response = await SendCommandAsync(request);
            FormatAndPrintResponse(ipcCommand, response);
        }
        catch (TimeoutException)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("[Error] Failed to connect to the Mars.Daemon service.");
            Console.WriteLine("Make sure the service is running with administrator privileges.");
            Console.ResetColor();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Critical error] {ex.Message}");
        }
    }

    private static async Task ExecuteGetModules()
    {
        try
        {
            var request = new IpcRequest { Command = "GET-MODULES" };
            var response = await SendCommandAsync(request);
            if (response == null || !response.IsSuccess || string.IsNullOrWhiteSpace(response.Data))
            {
                Console.WriteLine("Failed to get modules list.");
                return;
            }

            var modules = JsonSerializer.Deserialize<List<ModuleInfo>>(response.Data);
            if (modules == null || modules.Count == 0)
            {
                Console.WriteLine("No modules found.");
                return;
            }

            Console.WriteLine(string.Format("{0,-30} {1,-20} {2}", "Module Name", "Package", "Status"));
            Console.WriteLine(new string('-', 65));
            foreach (var m in modules)
            {
                var status = m.IsDisabled ? "Disabled" : "Enabled";
                var statusColor = m.IsDisabled ? ConsoleColor.DarkGray : ConsoleColor.Green;
                
                Console.Write(string.Format("{0,-30} {1,-20} ", m.Name, m.PackageName));
                Console.ForegroundColor = statusColor;
                Console.WriteLine(status);
                Console.ResetColor();
            }
        }
        catch (TimeoutException)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("[Error] Failed to connect to the Mars.Daemon service.");
            Console.ResetColor();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Error] {ex.Message}");
        }
    }

    private static async Task ExecuteSetModuleState(string packageName, bool disable)
    {
        try
        {
            var request = new IpcRequest { Command = "GET-MODULES" };
            var response = await SendCommandAsync(request);
            if (response == null || !response.IsSuccess || string.IsNullOrWhiteSpace(response.Data))
            {
                Console.WriteLine("Failed to get modules list from daemon.");
                return;
            }

            var modules = JsonSerializer.Deserialize<List<ModuleInfo>>(response.Data);
            var module = modules?.FirstOrDefault(m => m.PackageName.Equals(packageName, StringComparison.OrdinalIgnoreCase));

            if (module == null)
            {
                Console.WriteLine($"Module '{packageName}' not found.");
                return;
            }

            if (module.IsDisabled == disable)
            {
                Console.WriteLine($"Module '{packageName}' is already {(disable ? "disabled" : "enabled")}.");
                return;
            }

            module.IsDisabled = disable;

            var setRequest = new IpcRequest 
            { 
                Command = "SET-MODULE-STATE",
                Payload = JsonSerializer.Serialize(module)
            };

            var setResponse = await SendCommandAsync(setRequest);
            if (setResponse != null && setResponse.IsSuccess)
            {
                Console.WriteLine($"Successfully {(disable ? "disabled" : "enabled")} module: {module.Name}");
            }
            else
            {
                Console.WriteLine($"[Error] {setResponse?.Message ?? "Unknown error"}");
            }
        }
        catch (TimeoutException)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("[Error] Failed to connect to the Mars.Daemon service.");
            Console.ResetColor();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Error] {ex.Message}");
        }
    }

    private static async Task ExecuteInstallModule(List<string> packageNames)
    {
        try
        {
            Console.WriteLine($"Attempting to install {packageNames.Count} module(s)...");
            var request = new IpcRequest 
            { 
                Command = "INSTALL-MODULE",
                Payload = JsonSerializer.Serialize(packageNames)
            };
            var response = await SendCommandAsync(request);
            if (response != null && response.IsSuccess)
            {
                Console.WriteLine("Installation request processed successfully.");
            }
            else
            {
                Console.WriteLine($"[Error] {response?.Message ?? "Failed to process installation"}");
            }
        }
        catch (TimeoutException)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("[Error] Failed to connect to the Mars.Daemon service.");
            Console.ResetColor();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Error] {ex.Message}");
        }
    }

    private static async Task ExecuteDeleteModule(string packageName)
    {
        try
        {
            Console.WriteLine($"Attempting to delete module: {packageName}");
            var request = new IpcRequest 
            { 
                Command = "DELETE-MODULE",
                Payload = packageName
            };
            var response = await SendCommandAsync(request);
            if (response != null && response.IsSuccess)
            {
                Console.WriteLine($"Successfully deleted module: {packageName}");
            }
            else
            {
                Console.WriteLine($"[Error] {response?.Message ?? "Failed to delete module"}");
            }
        }
        catch (TimeoutException)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("[Error] Failed to connect to the Mars.Daemon service.");
            Console.ResetColor();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Error] {ex.Message}");
        }
    }

    /// <summary>
    /// Sends a command through Named Pipes and waits for a response.
    /// </summary>
    private static async Task<IpcResponse?> SendCommandAsync(IpcRequest request)
    {
        await using var pipeClient = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        
        await pipeClient.ConnectAsync(TimeoutMs);

        await using var writer = new StreamWriter(pipeClient, leaveOpen: true) { AutoFlush = true };
        using var reader = new StreamReader(pipeClient, leaveOpen: true);
        
        var requestJson = JsonSerializer.Serialize(request);
        await writer.WriteLineAsync(requestJson);
        
        var responseJson = await reader.ReadLineAsync();
        if (string.IsNullOrWhiteSpace(responseJson)) return null;

        try
        {
            return JsonSerializer.Deserialize<IpcResponse>(responseJson);
        }
        catch
        {
            return new IpcResponse { IsSuccess = false, Message = "Invalid response format from daemon." };
        }
    }

    /// <summary>
    /// Parses the JSON response and prints it in a readable format.
    /// </summary>
    private static void FormatAndPrintResponse(string command, IpcResponse? response)
    {
        if (response == null)
        {
            Console.WriteLine("Received an empty response from the service.");
            return;
        }

        if (!response.IsSuccess)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[Error] {response.Message}");
            Console.ResetColor();
            return;
        }
        
        if (command == "DUMP-SCHEMA")
        {
            Console.WriteLine(response.Data);
            return;
        }

        try
        {
            if (string.IsNullOrWhiteSpace(response.Data))
            {
                Console.WriteLine("No data returned.");
                return;
            }

            var json = JsonNode.Parse(response.Data);
            if (json == null) return;

            var isRunning = json["IsRunning"]?.GetValue<bool>() ?? false;
            var state = json["State"]?.GetValue<string>() ?? "Unknown";
            var error = json["LastError"]?.GetValue<string>();
            
            Console.Write("Process status: ");
            if (isRunning)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("Running");
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine("Stopped");
            }
            Console.ResetColor();

            Console.WriteLine($"Current state: {state}");

            if (!string.IsNullOrWhiteSpace(error))
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Last error: {error}");
                Console.ResetColor();
            }
            
        }
        catch (JsonException)
        {
            Console.WriteLine(response.Data);
        }
    }

    private static async Task ExecuteSetConfig(string command, string payload)
    {
        try
        {
            var request = new IpcRequest 
            { 
                Command = command,
                Payload = payload
            };
            var response = await SendCommandAsync(request);
            if (response != null && response.IsSuccess)
            {
                Console.WriteLine(response.Message);
            }
            else
            {
                Console.WriteLine($"[Error] {response?.Message ?? "Failed to update configuration"}");
            }
        }
        catch (TimeoutException)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("[Error] Failed to connect to the Mars.Daemon service.");
            Console.ResetColor();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Error] {ex.Message}");
        }
    }

    private static async Task ExecuteGetConfig(string command)
    {
        try
        {
            var request = new IpcRequest { Command = command };
            var response = await SendCommandAsync(request);
            if (response != null && response.IsSuccess)
            {
                Console.WriteLine(response.Data);
            }
            else
            {
                Console.WriteLine($"[Error] {response?.Message ?? "Failed to get configuration"}");
            }
        }
        catch (TimeoutException)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("[Error] Failed to connect to the Mars.Daemon service.");
            Console.ResetColor();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Error] {ex.Message}");
        }
    }

    private static void ShowHelp()
    {
        Console.WriteLine("MARS Command Line Interface");
        Console.WriteLine("Usage: mars-cli [command]");
        Console.WriteLine();
        Console.WriteLine("Commands:");
        Console.WriteLine("  --start                          Starts the MARS Python server");
        Console.WriteLine("  --stop                           Stops the MARS server");
        Console.WriteLine("  --status                         Shows the current server state");
        Console.WriteLine("  --schema                         Generates and prints the server module JSON schema");
        Console.WriteLine("  --modules                        Lists all installed modules and their status");
        Console.WriteLine("  --enable-module <package>        Enables a specific module");
        Console.WriteLine("  --disable-module <package>       Disables a specific module");
        Console.WriteLine("  --delete-module <package>        Permanently deletes a module");
        Console.WriteLine("  --get-host                       Gets the current IP address of the server");
        Console.WriteLine("  --get-port                       Gets the current port of the server");
        Console.WriteLine("  --set-host <ip>                  Sets the IP address for the server");
        Console.WriteLine("  --set-port <port>                Sets the port for the server");
        Console.WriteLine("  --get-autostart                  Gets whether the server starts automatically with the daemon");
        Console.WriteLine("  --set-autostart <true|false>     Enables or disables server auto-start on daemon startup");
        Console.WriteLine("  --set-password [password]        Sets the server password (provide empty to remove)");
        Console.WriteLine("  --install-module <name1> [name2] Installs one or more modules by their package names");
    }
}