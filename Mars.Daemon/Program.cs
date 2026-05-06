using Mars.Daemon.Services;
using System.Text;

Directory.SetCurrentDirectory(AppContext.BaseDirectory);
if (Environment.UserInteractive)
{
    try 
    {
        Console.OutputEncoding = Encoding.UTF8;
        Console.InputEncoding = Encoding.UTF8;
    } 
    catch { /* ignored */ }
}
if (!Environment.IsPrivilegedProcess)
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine("[FATAL] Mars.Daemon MUST be run as Administrator (Windows) or root (Linux).");
    Console.ResetColor();
    Environment.Exit(1);
}

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddSingleton<DaemonConfigService>();
builder.Services.AddSingleton<PythonRuntimeService>();
builder.Services.AddSingleton<PythonManager>();
builder.Services.AddSingleton<ModulesService>();
builder.Services.AddSingleton<NetworkService>();
builder.Services.AddHostedService<IpcServerWorker>();


builder.Services.AddWindowsService(options => { options.ServiceName = "Mars Daemon Service"; });
builder.Services.AddSystemd();

var host = builder.Build();
host.Run();