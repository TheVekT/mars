using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;

namespace Mars.UI.Services;

public interface ILogReaderService
{
    /// <summary>
    /// Reads the entire content of the log file.
    /// </summary>
    Task<string> ReadAllAsync(string path);

    /// <summary>
    /// Continuously monitors the log file for new entries and notifies via callback.
    /// </summary>
    void StartTail(string path, Action<string> onNewLines, CancellationToken ct);
}

public class LogReaderService : ILogReaderService
{
    public async Task<string> ReadAllAsync(string path)
    {
        if (!File.Exists(path)) return string.Empty;

        try
        {
            // Use FileShare.ReadWrite to allow reading while the daemon is writing
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            return await reader.ReadToEndAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[LogReaderService] Error reading log: {ex.Message}");
            return string.Empty;
        }
    }

    public void StartTail(string path, Action<string> onNewLines, CancellationToken ct)
    {
        Task.Run(async () =>
        {
            long lastPosition = 0;

            // First read: catch up with existing content
            if (File.Exists(path))
            {
                var initialContent = await ReadAllAsync(path);
                if (!string.IsNullOrEmpty(initialContent))
                {
                    var snapshot = initialContent;
                    Dispatcher.UIThread.Post(() => onNewLines?.Invoke(snapshot));
                    lastPosition = new FileInfo(path).Length;
                }
            }

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    if (File.Exists(path))
                    {
                        var fileInfo = new FileInfo(path);
                        
                        if (fileInfo.Length > lastPosition)
                        {
                            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                            stream.Position = lastPosition;
                            
                            using var reader = new StreamReader(stream, Encoding.UTF8);
                            var newContent = await reader.ReadToEndAsync();
                            
                            if (!string.IsNullOrEmpty(newContent))
                            {
                                var snapshot = newContent;
                                Dispatcher.UIThread.Post(() => onNewLines?.Invoke(snapshot));
                            }
                            
                            lastPosition = stream.Position;
                        }
                        else if (fileInfo.Length < lastPosition)
                        {
                            // File was probably truncated or rotated
                            lastPosition = 0;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[LogReaderService] Tail error: {ex.Message}");
                }

                await Task.Delay(1000, ct);
            }
        }, ct);
    }
}
