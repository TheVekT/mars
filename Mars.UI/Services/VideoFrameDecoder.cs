using System;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Mars.UI.Services;

/// <summary>
/// Decodes raw H264 NAL units via FFmpeg process, outputting BGRA frames.
/// Replaces LibVLC for video to eliminate imem demuxer errors and reduce latency.
/// </summary>
public class VideoFrameDecoder : IDisposable
{
    private Process? _ffmpeg;
    private int _width;
    private int _height;
    private readonly TaskCompletionSource<(int w, int h)> _resolutionTcs = new();
    private CancellationTokenSource? _cts;

    /// <summary>
    /// Fired when a decoded BGRA frame is ready. Args: (pixelData, width, height)
    /// </summary>
    public event Action<byte[], int, int>? OnFrame;

    public void Start()
    {
        _cts = new CancellationTokenSource();

        var psi = new ProcessStartInfo
        {
            FileName = "ffmpeg",
            Arguments = "-f h264 -probesize 32768 -analyzeduration 0 -fflags nobuffer+fastseek -flags low_delay -flags2 fast " +
                        "-i pipe:0 -f rawvideo -pix_fmt bgra -threads 1 -vsync passthrough pipe:1",
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        _ffmpeg = Process.Start(psi);
        if (_ffmpeg == null) throw new InvalidOperationException("Failed to start FFmpeg.");

        _ = Task.Run(() => ParseStderr(_cts.Token));
        _ = Task.Run(() => ReadFrames(_cts.Token));
    }

    /// <summary>
    /// Feed raw H264 data from WebSocket into FFmpeg's stdin.
    /// </summary>
    public void Feed(byte[] data, int offset, int count)
    {
        try
        {
            _ffmpeg?.StandardInput.BaseStream.Write(data, offset, count);
            _ffmpeg?.StandardInput.BaseStream.Flush();
        }
        catch { }
    }

    private async Task ParseStderr(CancellationToken ct)
    {
        try
        {
            var reader = _ffmpeg!.StandardError;
            while (!ct.IsCancellationRequested)
            {
                string? line = await reader.ReadLineAsync(ct);
                if (line == null) break;

                // Generic regex for "1920x1080" with sanity checks
                var match = Regex.Match(line, @"(\d{3,5})x(\d{3,5})");
                if (match.Success && !_resolutionTcs.Task.IsCompleted)
                {
                    int w = int.Parse(match.Groups[1].Value);
                    int h = int.Parse(match.Groups[2].Value);
                    if (w >= 160 && h >= 120) // Skip small or invalid matches
                    {
                        _width = w;
                        _height = h;
                        _resolutionTcs.TrySetResult((w, h));
                    }
                }
            }
        }
        catch { }
    }

    private async Task ReadFrames(CancellationToken ct)
    {
        try
        {
            var (w, h) = await _resolutionTcs.Task.WaitAsync(ct);
            int frameSize = w * h * 4; // BGRA = 4 bytes per pixel
            byte[] frameBuffer = new byte[frameSize];
            var stdout = _ffmpeg!.StandardOutput.BaseStream;

            while (!ct.IsCancellationRequested)
            {
                int totalRead = 0;
                while (totalRead < frameSize)
                {
                    int read = await stdout.ReadAsync(frameBuffer, totalRead, frameSize - totalRead, ct);
                    if (read == 0) return;
                    totalRead += read;
                }

                byte[] frameCopy = new byte[frameSize];
                Buffer.BlockCopy(frameBuffer, 0, frameCopy, 0, frameSize);
                OnFrame?.Invoke(frameCopy, w, h);
            }
        }
        catch (OperationCanceledException) { }
        catch { }
    }

    public void Dispose()
    {
        _cts?.Cancel();

        try
        {
            if (_ffmpeg != null && !_ffmpeg.HasExited)
            {
                try { _ffmpeg.StandardInput.Close(); } catch { }
                if (!_ffmpeg.WaitForExit(1000))
                    _ffmpeg.Kill();
            }
            _ffmpeg?.Dispose();
        }
        catch { }

        _ffmpeg = null;
        _cts?.Dispose();
        _cts = null;
    }
}
