using System;
using System.Threading.Tasks;

namespace Mars.UI.Services;

public interface IRemoteControlService : IDisposable
{
    bool IsConnected { get; }
    
    /// <summary>
    /// Fired when a decoded BGRA video frame is ready. Args: (pixelData, width, height)
    /// </summary>
    event Action<byte[], int, int>? OnVideoFrame;
    
    Task ConnectAsync(string ip, string port, string token);
    void Disconnect();
    
    void SendCommand(object command);
    void SetQuality(string mode);
    void ToggleCursor(bool state);
    void SetAudioStream(bool state);
    void SetClipboardSync(bool enabled);
}
