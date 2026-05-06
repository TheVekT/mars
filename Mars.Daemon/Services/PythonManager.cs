using System.Diagnostics;
using System.Net.Http;

namespace Mars.Daemon.Services;

/// <summary>
/// Manages the lifecycle of the Python server process: start, stop, status, and schema dump.
/// All configuration and runtime concerns are delegated to the injected services.
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

    // -------------------------------------------------------------------------
    // State
    // -------------------------------------------------------------------------

    /// <summary>Gets the current textual state of the server process.</summary>
    public string CurrentState { get; private set; } = "Stopped";

    /// <summary>Returns true while the Python process is alive.</summary>
    public bool IsRunning => _process != null && !_process.HasExited;

    /// <summary>Gets the last error message produced during startup or runtime.</summary>
    public string LastError { get; private set; } = string.Empty;

    // -------------------------------------------------------------------------
    // Forwarded path properties (used by IPC worker)
    // -------------------------------------------------------------------------

    public string ModulesDirectory => _config.ModulesDirectory;
    public string GetLogPath() => _config.LogPath;
    public string GetConfigPath() => _config.ConfigPath;
    public bool GetAutostart() => _config.GetAutostart();
    public bool SetAutostart(bool enabled) => _config.SetAutostart(enabled);
    public string GetServerConfig(string key, string def) => _config.GetPythonArg(key, def);
    public bool UpdateServerConfig(string key, string value) => _config.SetPythonArg(key, value);
    public bool RemoveServerConfig(string key) => _config.RemovePythonArg(key);

    // -------------------------------------------------------------------------
    // Lifecycle
    // -------------------------------------------------------------------------

    /// <summary>Starts the Python server process.</summary>
    public void StartServer()
    {
        if (IsRunning) return;

        CurrentState = "Starting...";
        LastError = string.Empty;

        var pythonExe = _runtime.PythonExePath;
        var serverScript = _runtime.ServerScriptPath;

        Console.WriteLine($"[Daemon] Python: {pythonExe}");
        Console.WriteLine($"[Daemon] Script: {serverScript}");

        if (!File.Exists(pythonExe))
        {
            CurrentState = "Error";
            LastError = $"Python executable not found: {pythonExe}";
            return;
        }

        if (!File.Exists(serverScript))
        {
            CurrentState = "Error";
            LastError = $"Server script not found: {serverScript}";
            return;
        }

        // Pre-flight checks
        _config.EnsureConfigExists();
        _runtime.EnsureExecutionPermissions();
        _runtime.EnsurePipInstalled();
        _networkService.ConfigureFirewall();

        if (_config.GetDisableIncompatibleModules())
        {
            _modulesService.DisableIncompatibleModules();
        }

        var serverArgs = _config.GetPythonArgs();

        var startInfo = new ProcessStartInfo
        {
            FileName = pythonExe,
            Arguments = $"\"{serverScript}\" {serverArgs}",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(serverScript)!
        };

        _process = new Process { StartInfo = startInfo };
        _process.OutputDataReceived += HandleOutput;
        _process.ErrorDataReceived += HandleOutput;

        try
        {
            _process.Start();
            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();
        }
        catch (Exception ex)
        {
            CurrentState = "Start failed";
            LastError = ex.Message;
        }
    }

    /// <summary>
    /// Gracefully stops the server via its local HTTP endpoint.
    /// Falls back to Kill() if the process doesn't exit within the timeout.
    /// </summary>
    public void StopServer()
    {
        if (_process == null || _process.HasExited)
        {
            CurrentState = "Stopped";
            return;
        }

        CurrentState = "Stopping...";
        Console.WriteLine("[Daemon] Requesting Python server shutdown via HTTP...");

        var port = _config.GetPythonArg("--port", "8000");
        var shutdownUrl = $"http://127.0.0.1:{port}/admin/shutdown";

        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
            http.PostAsync(shutdownUrl, null).Wait();
            Console.WriteLine("[Daemon] Shutdown request sent. Waiting for process to exit...");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Daemon] HTTP shutdown failed ({ex.Message}), falling back to Kill.");
        }

        if (!_process.WaitForExit(5000))
        {
            Console.WriteLine("[Daemon] Process did not exit in time, killing forcefully.");
            try { _process.Kill(entireProcessTree: true); } catch { }
            _process.WaitForExit(2000);
        }

        CurrentState = "Stopped";
        Console.WriteLine("[Daemon] Python server stopped.");
    }

    /// <summary>Runs the server with --dump-schema and returns the JSON output.</summary>
    public string DumpSchema()
    {
        var pythonExe = _runtime.PythonExePath;
        var serverScript = _runtime.ServerScriptPath;

        if (!File.Exists(pythonExe) || !File.Exists(serverScript))
            return "{\"error\": \"Runtime files not found\"}";

        var startInfo = new ProcessStartInfo
        {
            FileName = pythonExe,
            Arguments = $"\"{serverScript}\" --dump-schema",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(serverScript)!
        };

        try
        {
            using var process = Process.Start(startInfo);
            if (process == null) return string.Empty;

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(5000);

            return output
                .Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.RemoveEmptyEntries)
                .LastOrDefault(l => { var t = l.TrimStart(); return t.StartsWith('{') || t.StartsWith('['); })
                ?? string.Empty;
        }
        catch (Exception ex)
        {
            return $"{{\"error\": \"{ex.Message}\"}}";
        }
    }

    public void Dispose()
    {
        StopServer();
        _process?.Dispose();
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    private void HandleOutput(object sender, DataReceivedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(e.Data)) return;
        Console.WriteLine($"[Python] {e.Data}");

        if (e.Data.Contains("Installing missing library", StringComparison.OrdinalIgnoreCase))
            CurrentState = "Installing dependencies...";
        else if (e.Data.Contains("Library '", StringComparison.OrdinalIgnoreCase) &&
                 e.Data.Contains("installed successfully", StringComparison.OrdinalIgnoreCase))
            CurrentState = "Loading modules...";
        else if (e.Data.StartsWith("Found module:", StringComparison.OrdinalIgnoreCase) ||
                 e.Data.StartsWith("Skipping", StringComparison.OrdinalIgnoreCase) ||
                 e.Data.StartsWith("Module loaded successfully:", StringComparison.OrdinalIgnoreCase))
            CurrentState = "Loading modules...";
        else if (e.Data.Contains("MARS server is ready on", StringComparison.OrdinalIgnoreCase) ||
                 e.Data.Contains("Application startup complete", StringComparison.OrdinalIgnoreCase) ||
                 e.Data.Contains("Uvicorn running on", StringComparison.OrdinalIgnoreCase))
            CurrentState = "Running";
        else if (e.Data.Contains("Failed to install", StringComparison.OrdinalIgnoreCase) ||
                 e.Data.Contains("Failed to load module", StringComparison.OrdinalIgnoreCase) ||
                 e.Data.Contains("[ERROR]", StringComparison.OrdinalIgnoreCase) ||
                 e.Data.Contains("Exception", StringComparison.OrdinalIgnoreCase))
        {
            CurrentState = "Error";
            LastError = e.Data;
        }
    }
}