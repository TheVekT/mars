using System;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Security.AccessControl;
using System.Security.Principal;

namespace Mars.Daemon.Services;

/// <summary>
/// Manages the Python server process lifecycle across Windows and Linux.
/// Handles session isolation on both platforms.
/// </summary>
public class PythonManager : IDisposable
{
    private readonly DaemonConfigService _config;
    private readonly PythonRuntimeService _runtime;
    private readonly NetworkService _networkService;
    private readonly ModulesService _modulesService;
    private Process? _process;

    public PythonManager(DaemonConfigService config, PythonRuntimeService runtime, ModulesService modulesService, NetworkService networkService)
    {
        _config = config;
        _runtime = runtime;
        _modulesService = modulesService;
        _networkService = networkService;
        
        if (_config.GetDisableIncompatibleModules())
            _modulesService.DisableIncompatibleModules();
    }

    public string CurrentState { get; private set; } = "Stopped";
    public bool IsRunning => CurrentState == "Running" || CurrentState == "Starting..." || (_process != null && !_process.HasExited);
    public string LastError { get; private set; } = string.Empty;

    public string ModulesDirectory => _config.ModulesDirectory;
    public string GetLogPath() => _config.LogPath;
    public string GetConfigPath() => _config.ConfigPath;
    public bool GetAutostart() => _config.GetAutostart();
    public bool SetAutostart(bool enabled) => _config.SetAutostart(enabled);
    public string GetServerConfig(string key, string def) => _config.GetPythonArg(key, def);
    public bool UpdateServerConfig(string key, string value) => _config.SetPythonArg(key, value);
    public bool RemoveServerConfig(string key) => _config.RemovePythonArg(key);

    public void StartServer()
    {
        if (IsRunning) return;

        string pythonExe = _runtime.PythonExePath;
        string serverScript = _runtime.ServerScriptPath;
        string serverArgs = _config.GetPythonArgs();

        _process = null;
        try
        {
            string tempDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "temp");
            if (!Directory.Exists(tempDir)) Directory.CreateDirectory(tempDir);
            
            string pidFile = Path.Combine(tempDir, "server.pid");
            if (File.Exists(pidFile)) File.Delete(pidFile);

            string workingDir = Path.GetDirectoryName(serverScript)!;

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                string batPath = Path.Combine(tempDir, "run_server.bat");
                string vbsPath = Path.Combine(tempDir, "run_hidden.vbs");
                
                string args = serverArgs;
                if (!args.Contains("--host")) args = $"--host {_config.GetPythonArg("host", "0.0.0.0")} " + args;
                if (!args.Contains("--port")) args = $"--port {_config.GetPythonArg("port", "8000")} " + args;
                
                args += $" --pid-file \"{pidFile}\"";
                
                string systemRoot = Environment.GetEnvironmentVariable("SystemRoot") ?? "C:\\Windows";
                string systemDrive = Environment.GetEnvironmentVariable("SystemDrive") ?? "C:";
                string pythonDir = Path.GetDirectoryName(pythonExe)!;

                string batContent = "@echo off\r\n" +
                                   $"set SYSTEMROOT={systemRoot}\r\n" +
                                   $"set SYSTEMDRIVE={systemDrive}\r\n" +
                                   "set TEMP=%USERPROFILE%\\AppData\\Local\\Temp\r\n" +
                                   "set TMP=%USERPROFILE%\\AppData\\Local\\Temp\r\n" +
                                   $"set PATH={pythonDir};{systemRoot}\\system32;{systemRoot};{systemRoot}\\System32\\Wbem;%PATH%\r\n" +
                                   $"cd /d \"{workingDir}\"\r\n" +
                                   $"\"{pythonExe}\" \"{serverScript}\" {args}";
                
                File.WriteAllText(batPath, batContent);

                string vbsContent = "Set WshShell = CreateObject(\"WScript.Shell\")\r\n" +
                                   $"WshShell.Run \"cmd.exe /c \" & Chr(34) & \"{batPath}\" & Chr(34), 0, False\r\n" +
                                   "Set WshShell = Nothing";
                File.WriteAllText(vbsPath, vbsContent);

                ApplyPermissions(batPath, vbsPath, tempDir);

                string interactiveName = GetInteractiveGroupName();
                string taskName = "MarsServerTask";
                string createCmd = $"/c schtasks /create /tn {taskName} /tr \"wscript.exe \\\"{vbsPath}\\\"\" /sc once /st 00:00 /ru \"{interactiveName}\" /rl HIGHEST /f";
                
                Process.Start(new ProcessStartInfo { FileName = "cmd.exe", Arguments = createCmd, CreateNoWindow = true, UseShellExecute = false })?.WaitForExit();
                Process.Start(new ProcessStartInfo { FileName = "cmd.exe", Arguments = $"/c schtasks /run /tn {taskName}", CreateNoWindow = true, UseShellExecute = false })?.WaitForExit();
                
                CurrentState = "Starting...";
            }
            else
            {
                // Linux logic
                string? display = ":0";
                string? xauth = null;
                string? userUid = "1000";
                string? userName = null;

                try {
                    var loginctl = Process.Start(new ProcessStartInfo { 
                        FileName = "loginctl", 
                        Arguments = "list-sessions --no-legend", 
                        RedirectStandardOutput = true, 
                        UseShellExecute = false 
                    });
                    string? line = loginctl?.StandardOutput.ReadLine();
                    if (line != null) {
                        var parts = line.Trim().Split(' ', System.StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length > 2) {
                            var sessionId = parts[0];
                            var sessionInfo = Process.Start(new ProcessStartInfo { 
                                FileName = "loginctl", 
                                Arguments = $"show-session {sessionId} -p Name -p User -p Display", 
                                RedirectStandardOutput = true, 
                                UseShellExecute = false 
                            });
                            var output = sessionInfo?.StandardOutput.ReadToEnd();
                            if (output != null) {
                                foreach (var infoLine in output.Split('\n')) {
                                    if (infoLine.StartsWith("User=")) userUid = infoLine.Split('=')[1].Trim();
                                    if (infoLine.StartsWith("Name=")) userName = infoLine.Split('=')[1].Trim();
                                    if (infoLine.StartsWith("Display=")) display = infoLine.Split('=')[1].Trim();
                                }
                            }
                        }
                    }
                } catch { }

                if (string.IsNullOrEmpty(userName)) userName = "user";

                var possibleXauths = new[] {
                    $"/run/user/{userUid}/xauth",
                    $"/run/user/{userUid}/gdm/Xauthority",
                    $"/home/{userName}/.Xauthority",
                    $"/root/.Xauthority"
                };

                foreach (var path in possibleXauths) {
                    if (File.Exists(path)) {
                        xauth = path;
                        break;
                    }
                }

                _process = new Process();
                _process.StartInfo.FileName = pythonExe;
                _process.StartInfo.Arguments = $"\"{serverScript}\" {serverArgs} --pid-file \"{pidFile}\"";
                _process.StartInfo.UseShellExecute = false;
                _process.StartInfo.WorkingDirectory = workingDir;
                
                _process.StartInfo.EnvironmentVariables["DISPLAY"] = string.IsNullOrEmpty(display) ? ":0" : display;
                if (!string.IsNullOrEmpty(xauth))
                    _process.StartInfo.EnvironmentVariables["XAUTHORITY"] = xauth;
                _process.StartInfo.EnvironmentVariables["XDG_RUNTIME_DIR"] = $"/run/user/{userUid}";
                _process.StartInfo.EnvironmentVariables["MARS_SESSION_USER"] = userName;
                
                _process.Start();
                CurrentState = "Starting...";
            }

            Task.Run(async () => {
                int attempts = 0;
                string portStr = _config.GetPythonArg("port", "8000");
                int port = int.Parse(portStr);
                while (attempts < 60) {
                    try {
                        using var client = new TcpClient();
                        await client.ConnectAsync("127.0.0.1", port); 
                        
                        if (_process == null && File.Exists(pidFile)) {
                            try {
                                int pid = int.Parse(File.ReadAllText(pidFile).Trim());
                                _process = Process.GetProcessById(pid);
                            } catch { }
                        }

                        CurrentState = "Running";
                        CleanupTask();
                        return;
                    } catch {
                        attempts++;
                        await Task.Delay(1000);
                    }
                }
                CurrentState = "Error";
                LastError = "Server failed to start (Timeout)";
            });
        }
        catch (Exception ex)
        {
            CurrentState = "Error";
            LastError = ex.Message;
        }
    }

    private void ApplyPermissions(params string[] paths)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;
        
        try {
            foreach (var path in paths) {
                var fileInfo = new FileInfo(path);
                var accessControl = fileInfo.GetAccessControl();
                accessControl.AddAccessRule(new FileSystemAccessRule(
                    new SecurityIdentifier(WellKnownSidType.WorldSid, null),
                    FileSystemRights.ReadAndExecute,
                    AccessControlType.Allow));
                fileInfo.SetAccessControl(accessControl);
            }
        } catch { }
    }

    private string GetInteractiveGroupName()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return "INTERACTIVE";
        
        try {
            var sid = new SecurityIdentifier(WellKnownSidType.InteractiveSid, null);
            return sid.Translate(typeof(NTAccount)).Value;
        } catch { }
        
        return "INTERACTIVE";
    }

    public void StopServer()
    {
        if (_process == null || _process.HasExited)
        {
            CurrentState = "Stopped";
            CleanupTask();
            return;
        }

        CurrentState = "Stopping...";
        var port = _config.GetPythonArg("port", "8000");
        var shutdownUrl = $"http://127.0.0.1:{port}/admin/shutdown";

        Task.Run(async () => {
            try {
                using var client = new System.Net.Http.HttpClient();
                await client.PostAsync(shutdownUrl, null);
            } catch { }

            int wait = 0;
            while (!_process.HasExited && wait < 5) {
                await Task.Delay(1000);
                wait++;
            }

            if (!_process.HasExited) {
                try { _process.Kill(); } catch { }
            }
            
            _process = null;
            CurrentState = "Stopped";
            CleanupTask();
        });
    }

    public string DumpSchema()
    {
        try
        {
            var info = new ProcessStartInfo
            {
                FileName = _runtime.PythonExePath,
                Arguments = $"\"{_runtime.ServerScriptPath}\" --dump-schema",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc = Process.Start(info);
            return proc?.StandardOutput.ReadToEnd() ?? string.Empty;
        }
        catch (Exception ex)
        {
            return $"{{\"error\": \"{ex.Message}\"}}";
        }
    }

    private void CleanupTask()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            try {
                Process.Start(new ProcessStartInfo { FileName = "cmd.exe", Arguments = "/c schtasks /delete /tn MarsServerTask /f", CreateNoWindow = true, UseShellExecute = false });
            } catch { }
        }
    }

    public void Dispose()
    {
        if (_process != null && !_process.HasExited)
            StopServer();
    }
}