using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Mars.UI.Services;

/// <summary>
/// Plays raw PCM audio (s16le, mono, 22050Hz) by piping it to FFmpeg/ffplay.
/// Replaces LibVLC audio to eliminate all imem demuxer errors.
/// </summary>
public class AudioPcmPlayer : IDisposable
{
    private Process? _ffplay;
    private bool _isMuted = true;

    public void Start()
    {
        try
        {
            _ffplay = Process.Start(new ProcessStartInfo
            {
                FileName = "ffplay",
                Arguments = "-f s16le -ar 22050 -ac 1 -nodisp -autoexit -loglevel quiet -probesize 1024 -analyzeduration 0 -fflags nobuffer+fastseek -flags low_delay -i pipe:0",
                RedirectStandardInput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });
        }
        catch
        {
            _ffplay = null;
        }
    }

    /// <summary>
    /// Feed raw PCM s16le audio data.
    /// </summary>
    public void Feed(byte[] data, int offset, int count)
    {
        if (_isMuted || _ffplay == null) return;
        
        try
        {
            _ffplay.StandardInput.BaseStream.Write(data, offset, count);
            _ffplay.StandardInput.BaseStream.Flush();
        }
        catch { }
    }

    public void SetMuted(bool muted)
    {
        _isMuted = muted;
    }

    public void Dispose()
    {
        try
        {
            if (_ffplay != null && !_ffplay.HasExited)
            {
                try { _ffplay.StandardInput.Close(); } catch { }
                if (!_ffplay.WaitForExit(1000))
                    _ffplay.Kill();
            }
            _ffplay?.Dispose();
        }
        catch { }
        _ffplay = null;
    }
}
