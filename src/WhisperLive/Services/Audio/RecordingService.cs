using NAudio.Wave;
using System.IO;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using WhisperLive.Infrastructure;
using WhisperLive.Models;

namespace WhisperLive.Services.Audio;

public sealed class RecordingService : IRecordingService
{
    private const double MaxOverlapSeconds = 1.5;
    private const double MaxOverlapFraction = 0.4;
    private const int ChannelCapacity = 5;

    private Channel<AudioChunkInfo> _channel = Channel.CreateBounded<AudioChunkInfo>(ChannelCapacity);
    private WasapiLoopbackCapture? _capture;
    private MemoryStream _buffer = new();
    private readonly object _lock = new();
    private int _chunkIndex;
    private double _offsetSeconds;
    private byte[] _overlapTail = [];

    private volatile bool _paused;
    public bool IsPaused => _paused;

    public event EventHandler<float>? AudioLevelChanged;
    private DateTime _lastLevelFire = DateTime.MinValue;

    public ChannelReader<AudioChunkInfo> Chunks => _channel.Reader;

    public Task StartAsync(RecordingOptions options, CancellationToken ct)
    {
        _channel = Channel.CreateBounded<AudioChunkInfo>(
            new BoundedChannelOptions(ChannelCapacity) { FullMode = BoundedChannelFullMode.DropOldest });
        _chunkIndex = 0;
        _offsetSeconds = 0;
        _buffer = new MemoryStream();
        _overlapTail = [];

        _capture = new WasapiLoopbackCapture();
        var waveFormat = _capture.WaveFormat;

        _capture.DataAvailable += (_, e) =>
        {
            if (e.BytesRecorded == 0 || _paused) return;
            lock (_lock)
                _buffer.Write(e.Buffer, 0, e.BytesRecorded);

            // Fire audio level at ~20fps for waveform animation.
            var now = DateTime.UtcNow;
            if ((now - _lastLevelFire).TotalMilliseconds >= 50)
            {
                _lastLevelFire = now;
                AudioLevelChanged?.Invoke(this, ComputeRms(e.Buffer, e.BytesRecorded, waveFormat));
            }
        };

        _capture.StartRecording();
        AppLogger.Info("Recording started — chunk={Seconds}s language={Language} model={Model}",
            options.ChunkDurationSeconds, options.Language, options.Model);
        _ = RunFlushLoopAsync(waveFormat, options, ct);
        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        _paused = false;
        _capture?.StopRecording();
        _capture?.Dispose();
        _capture = null;
        AppLogger.Info("Recording stopped — {Chunks} chunks sent", _chunkIndex);
        return Task.CompletedTask;
    }

    public Task PauseAsync()
    {
        _paused = true;
        AppLogger.Info("Recording paused at chunk #{Index}", _chunkIndex);
        return Task.CompletedTask;
    }

    public Task ResumeAsync()
    {
        _paused = false;
        AppLogger.Info("Recording resumed at chunk #{Index}", _chunkIndex);
        return Task.CompletedTask;
    }

    private async Task RunFlushLoopAsync(WaveFormat waveFormat, RecordingOptions options, CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(options.ChunkDurationSeconds));
        try
        {
            while (await timer.WaitForNextTickAsync(ct))
            {
                try { FlushChunk(waveFormat, options); }
                catch (Exception ex) { AppLogger.Warning(ex, "FlushChunk failed — audio chunk dropped"); }
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            // Final flush on stop: drain any audio buffered since the last tick.
            // TryComplete() must always run so ConsumeChunksAsync can exit cleanly.
            try { FlushChunk(waveFormat, options); }
            catch (Exception ex) { AppLogger.Warning(ex, "Final FlushChunk failed on stop"); }
            _channel.Writer.TryComplete();
        }
    }

    private void FlushChunk(WaveFormat waveFormat, RecordingOptions options)
    {
        byte[] freshData;
        lock (_lock)
        {
            if (_buffer.Length == 0) return;
            freshData = _buffer.ToArray();
            _buffer.SetLength(0);
            _buffer.Position = 0;
        }

        double overlapTarget = Math.Min(MaxOverlapSeconds, options.ChunkDurationSeconds * MaxOverlapFraction);
        double chunkOverlap = _overlapTail.Length > 0 ? overlapTarget : 0.0;
        byte[] pcmData;
        if (_overlapTail.Length > 0)
        {
            pcmData = new byte[_overlapTail.Length + freshData.Length];
            _overlapTail.CopyTo(pcmData, 0);
            freshData.CopyTo(pcmData, _overlapTail.Length);
        }
        else
        {
            pcmData = freshData;
        }

        int overlapBytes = (int)(overlapTarget * waveFormat.AverageBytesPerSecond);
        _overlapTail = freshData.Length >= overlapBytes
            ? freshData[^overlapBytes..]
            : freshData;

        double wavStartTime = _offsetSeconds - chunkOverlap;

        // Build WAV in memory — avoids temp-file disk I/O on the hot path.
        // WaveFileWriter closes the underlying MemoryStream on Dispose, but
        // MemoryStream.ToArray() still works on a disposed instance.
        var ms = new MemoryStream(pcmData.Length + 64);
        using (var writer = new WaveFileWriter(ms, waveFormat))
            writer.Write(pcmData, 0, pcmData.Length);
        byte[] wavBytes = ms.ToArray();

        AppLogger.Debug("Flushed chunk #{Index} — {Bytes} bytes (overlap={Overlap}s)",
            _chunkIndex, wavBytes.Length, chunkOverlap);

        // The channel is bounded with DropOldest, so TryWrite always succeeds — it
        // silently evicts the oldest queued chunk when full. Check Count first so a
        // real backlog eviction is logged instead of being invisible.
        if (_channel.Reader.Count >= ChannelCapacity)
            AppLogger.Warning("Audio chunk backlog full ({Capacity}) — evicting oldest queued chunk; transcript gap possible", ChannelCapacity);
        _channel.Writer.TryWrite(new AudioChunkInfo(wavBytes, _chunkIndex, wavStartTime, chunkOverlap));
        _offsetSeconds += options.ChunkDurationSeconds;
        _chunkIndex++;
    }

    /// <summary>
    /// Computes normalised RMS amplitude (0–1) from raw PCM or IEEE-float loopback buffers.
    /// WASAPI loopback typically delivers 32-bit IEEE float; 16-bit PCM is handled as fallback.
    /// Output is scaled so typical speech (~0.05 raw RMS) reads as ~0.3–0.6 for visible bars.
    /// </summary>
    private static float ComputeRms(byte[] buffer, int bytesRecorded, WaveFormat format)
    {
        if (bytesRecorded < 4) return 0f;
        double sum = 0;
        int n;
        if (format.BitsPerSample == 32)
        {
            n = bytesRecorded / 4;
            for (int i = 0; i < n * 4; i += 4)
            {
                double v = BitConverter.ToSingle(buffer, i);
                sum += v * v;
            }
        }
        else // 16-bit PCM fallback
        {
            n = bytesRecorded / 2;
            for (int i = 0; i < n * 2; i += 2)
            {
                double v = (short)(buffer[i] | (buffer[i + 1] << 8)) / 32768.0;
                sum += v * v;
            }
        }
        if (n == 0) return 0f;
        // Multiply by 5 so typical speech fills the bars nicely; clamp to 1.
        return (float)Math.Min(1.0, Math.Sqrt(sum / n) * 5.0);
    }

    public void Dispose()
    {
        _capture?.Dispose();
        _buffer.Dispose();
    }
}
