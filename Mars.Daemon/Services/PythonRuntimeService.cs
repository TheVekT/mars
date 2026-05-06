using System.Diagnostics;

namespace Mars.Daemon.Services;

/// <summary>
/// Manages the bundled Python runtime: resolves paths, ensures pip is installed,
/// and sets execution permissions on Linux.
/// This service has no knowledge of the server process itself.
/// </summary>
public class PythonRuntimeService
{
    private readonly string _basePath;

    public PythonRuntimeService()
    {
        _basePath = AppDomain.CurrentDomain.BaseDirectory;
    }

    // -------------------------------------------------------------------------
    // Path resolution
    // -------------------------------------------------------------------------

    /// <summary>Returns the path to the bundled Python executable.</summary>
    public string PythonExePath =>
        OperatingSystem.IsWindows()
            ? Path.Combine(_basePath, "python-runtime", "win64", "python.exe")
            : Path.Combine(_basePath, "python-runtime", "linux", "bin", "python");

    /// <summary>Returns the path to the server entry script.</summary>
    public string ServerScriptPath => Path.Combine(_basePath, "mars-server", "server.py");

    // -------------------------------------------------------------------------
    // Pre-flight checks
    // -------------------------------------------------------------------------

    /// <summary>Ensures the Python executable and its tools have execution permissions on Linux (chmod 755).</summary>
    public void EnsureExecutionPermissions()
    {
        if (OperatingSystem.IsWindows()) return;

        try
        {
            var binDir = Path.Combine(_basePath, "python-runtime", "linux", "bin");
            if (!Directory.Exists(binDir)) return;

            // List of binaries that must be executable
            var binaries = new[] { "python", "python3.12", "pip", "pip3", "pip3.12" };

            foreach (var binaryName in binaries)
            {
                var path = Path.Combine(binDir, binaryName);
                if (File.Exists(path))
                {
                    File.SetUnixFileMode(path,
                        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                        UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                        UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Runtime] Warning: failed to set execution permissions: {ex.Message}");
        }
    }

    /// <summary>
    /// Verifies that pip is functional. If not, restores it using the bundled get-pip.py.
    /// Windows-only (Linux bundles always have pip).
    /// </summary>
    public void EnsurePipInstalled()
    {
        if (!OperatingSystem.IsWindows()) return;

        var pythonDir = Path.Combine(_basePath, "python-runtime", "win64");
        var pythonExe = Path.Combine(pythonDir, "python.exe");
        var pipExe = Path.Combine(pythonDir, "Scripts", "pip.exe");
        var getPipScript = Path.Combine(pythonDir, "get-pip.py");

        if (IsPipWorking(pipExe)) return;

        Console.WriteLine("[Runtime] pip is missing or corrupted. Attempting recovery...");

        if (!File.Exists(getPipScript))
        {
            Console.WriteLine("[Runtime] Error: get-pip.py was not found — cannot restore pip.");
            return;
        }

        try
        {
            var info = new ProcessStartInfo
            {
                FileName = pythonExe,
                Arguments = $"\"{getPipScript}\" --no-warn-script-location",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = pythonDir
            };

            using var proc = Process.Start(info);
            proc?.WaitForExit(60_000);

            Console.WriteLine(proc?.ExitCode == 0
                ? "[Runtime] pip restored successfully."
                : $"[Runtime] pip installation finished with exit code: {proc?.ExitCode}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Runtime] Critical pip recovery error: {ex.Message}");
        }
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    private static bool IsPipWorking(string pipExe)
    {
        if (!File.Exists(pipExe)) return false;
        try
        {
            var info = new ProcessStartInfo
            {
                FileName = pipExe,
                Arguments = "--version",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc = Process.Start(info);
            proc?.WaitForExit(3_000);
            return proc?.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}
