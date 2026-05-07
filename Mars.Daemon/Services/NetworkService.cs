using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Mars.Daemon.Services;

/// <summary>
/// Manages system-level network configurations, such as firewall rules.
/// </summary>
public class NetworkService
{
    private readonly DaemonConfigService _config;

    public NetworkService(DaemonConfigService config)
    {
        _config = config;
    }

    /// <summary>
    /// Configures the system firewall to allow traffic on the configured port.
    /// </summary>
    public void ConfigureFirewall()
    {
        var portStr = _config.GetPythonArg("--port", "8000");
        if (!int.TryParse(portStr, out int port))
        {
            Console.WriteLine($"[Network] Invalid port configured: {portStr}");
            return;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            ConfigureWindowsFirewall(port);
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            ConfigureLinuxFirewall(port);
        }
        else
        {
            Console.WriteLine($"[Network] Firewall configuration is not supported on {RuntimeInformation.OSDescription}");
        }
    }

    private void ConfigureWindowsFirewall(int port)
    {
        string ruleName = $"MARS Server Port {port}";

        // Check if rule exists
        if (RunCommand("netsh", $"advfirewall firewall show rule name=\"{ruleName}\"") == 0)
        {
            Console.WriteLine($"[Network] Firewall rule '{ruleName}' already exists.");
            return;
        }

        Console.WriteLine($"[Network] Adding Windows Firewall rule for port {port}...");
        
        string args = $"advfirewall firewall add rule name=\"{ruleName}\" dir=in action=allow protocol=TCP localport={port} profile=any description=\"Allow inbound traffic for MARS Server\"";
        
        int exitCode = RunCommand("netsh", args);
        if (exitCode == 0)
        {
            Console.WriteLine($"[Network] Firewall rule '{ruleName}' added successfully.");
        }
        else
        {
            Console.WriteLine($"[Network] Failed to add firewall rule. Exit code: {exitCode}");
        }
    }

    private void ConfigureLinuxFirewall(int port)
    {
        if (HasCommand("ufw"))
        {
            // Check if ufw is active
            if (GetCommandOutput("ufw", "status").Contains("Status: active"))
            {
                Console.WriteLine($"[Network] Configuring UFW for port {port}...");
                RunCommand("ufw", $"allow {port}/tcp");
            }
            else
            {
                Console.WriteLine("[Network] UFW is not active, skipping.");
            }
        }
        else if (HasCommand("firewall-cmd"))
        {
            Console.WriteLine($"[Network] Configuring firewalld for port {port}...");
            RunCommand("firewall-cmd", $"--permanent --add-port={port}/tcp");
            RunCommand("firewall-cmd", "--reload");
        }
        else
        {
            Console.WriteLine("[Network] No supported firewall manager (ufw, firewalld) found. Please open port manually.");
        }
    }

    private int RunCommand(string command, string args)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = command,
                Arguments = args,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            process?.WaitForExit();
            return process?.ExitCode ?? -1;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Network] Error running command {command}: {ex.Message}");
            return -1;
        }
    }

    private string GetCommandOutput(string command, string args)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = command,
                Arguments = args,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            string output = process?.StandardOutput.ReadToEnd() ?? string.Empty;
            process?.WaitForExit();
            return output;
        }
        catch
        {
            return string.Empty;
        }
    }

    private bool HasCommand(string cmd)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "which",
                Arguments = cmd,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                startInfo.FileName = "where";
            }

            using var process = Process.Start(startInfo);
            process?.WaitForExit();
            return process?.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}
