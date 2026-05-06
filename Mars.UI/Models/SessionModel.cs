using System;

namespace Mars.UI.Models;

public class SessionModel
{
    public string Name { get; set; } = string.Empty;
    public string IpAddress { get; set; } = string.Empty;
    public string Port { get; set; } = string.Empty;
    public DateTime LastConnectedTime { get; set; }
    
    public string LastUsedPassword { get; set; } = string.Empty;
    public bool IsFavourite { get; set; }
    
    public string NetworkAddress => $"{IpAddress}:{Port}";
}