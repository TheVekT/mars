using System.Collections.Generic;

namespace Mars.Shared.Models;

public class IpcRequest
{
    public string Command { get; set; } = string.Empty;
    public string Payload { get; set; } = string.Empty;
    public Dictionary<string, string> Parameters { get; set; } = new();
}

public class IpcResponse
{
    public bool IsSuccess { get; set; }
    public string Message { get; set; } = string.Empty;
    public string Data { get; set; } = string.Empty;
}

public class DaemonStatus
{
    public bool IsRunning { get; set; }
    public string State { get; set; } = string.Empty;
    public string LastError { get; set; } = string.Empty;
}
