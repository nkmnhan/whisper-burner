using NAudio.Wave;
using System.Buffers;
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
    // Reused across flushes (grown once to the overlap size) instead of slicing a fresh array
    // every chunk. _overlapTailLen is the valid prefix length.
    private byte[] _overlapTail = [];
    private int _overlapTailLen;

    private volatile bool _paused;
    public bool IsPaused => _paused;

    // Set true only for an intentional StopAsync so the RecordingStopped handler can distinguish
    // a normal stop from an unexpected device loss.
    private volatile bool _stopRequested;

    public event EventHandler<float>? AudioLevelChanged;
    /// <summary>Raised when capture ends unexpectedly (device removed/changed), not on a normal stop.</summary>
    public event EventHandler<Exception?>? CaptureStopped;
    private DateTime _lastLevelFire = DateTime.MinValue;

    public ChannelReader<AudioChunkInfo> Chunks => _channel.Reader;

    public Task StartAsync(RecordingOptions options, CancellationToken ct)
    {
        _channel = Channel.CreateBounded<AudioChunkInfo>(
            new BoundedChannelOptions(ChannelCapacity) { FullMode = BoundedChannelFullMode.DropOldest });
        _chunkIndex = 0;
        _offsetSeconds = 0;
        _buffer = new MemoryStream();
        _overlapTailLen = 0;
        _stopRequested = false;

        _capture = new WasapiLoopbackCapture();
        var waveFormat = _capture.WaveFormat;

        _capture.DataAvailable += (_, e) =>
        {
            // Runs on NAudio's capture thread. Any exception escaping here is unhandled on a raw
            // background thread → hard process crash, so contain it and keep capturing.
            try
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
            }
            catch (Exception ex)
            {
                AppLogger.Warning(ex, "Audio DataAvailable handler error — chunk of audio dropped");
            }
        };

        _capture.RecordingStopped += OnRecordingStopped;

        try
        {
            _capture.StartRecording();
        }
        catch
        {
            _capture.Dispose();
            _capture = null;
            throw; // surfaced to RecordingManager.StartAsync, which rolls back and reports to the UI
        }

        AppLogger.Info("Recording started — chunk={Seconds}s language={Language} model={Model}",
            options.ChunkDurationSeconds, options.Language, options.Model);
        _ = RunFlushLoopAsync(waveFormat, options, ct);
        return Task.CompletedTask;
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        if (_stopRequested) return; // normal stop initiated by StopAsync
        AppLogger.Warning(e.Exception, "Audio capture stopped unexpectedly (playback device changed/removed?)");
        CaptureStopped?.Invoke(this, e.Exception);
    }

    public Task StopAsync()
    {
        _paused = false;
        _stopRequested = true;
        if (_capture is { } capture)
        {
            capture.RecordingStopped -= OnRecordingStopped;
            capture.StopRecording();
            capture.Dispose();
            _capture = null;
        }
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
        // The audio flush is by far the app's largest allocator: at 48kHz/32-bit-float stereo a
        // 7s chunk is ~2.7MB, and the naive path allocated ~5 such arrays per chunk straight onto the
        // Large Object Heap (~6GB/hr of gen-2 garbage over a long session). Rent the transient buffers
        // from ArrayPool and reuse the overlap tail; only the final WAV payload is a real allocation.
        byte[]? fresh = null;
        byte[]? pcm = null;
        int freshLen;
        lock (_lock)
        {
            freshLen = (int)_buffer.Length;
            if (freshLen == 0) return;
            fresh = ArrayPool<byte>.Shared.Rent(freshLen);
            _buffer.Position = 0;
            _ = _buffer.Read(fresh, 0, freshLen);
            _buffer.SetLength(0);
            _buffer.Position = 0;
        }

        try
        {
            double overlapTarget = Math.Min(MaxOverlapSeconds, options.ChunkDurationSeconds * MaxOverlapFraction);
            double chunkOverlap = _overlapTailLen > 0 ? overlapTarget : 0.0;

            int pcmLen = _overlapTailLen + freshLen;
            pcm = ArrayPool<byte>.Shared.Rent(pcmLen);
            if (_overlapTailLen > 0)
                Array.Copy(_overlapTail, 0, pcm, 0, _overlapTailLen);
            Array.Copy(fresh, 0, pcm, _overlapTailLen, freshLen);

            // Retain the last `overlapBytes` of fresh audio as the next chunk's overlap prefix.
            int overlapBytes = (int)(overlapTarget * waveFormat.AverageBytesPerSecond);
            int take = Math.Min(overlapBytes, freshLen);
            if (_overlapTail.Length < take)
                _overlapTail = new byte[take];
            Array.Copy(fresh, freshLen - take, _overlapTail, 0, take);
            _overlapTailLen = take;

            double wavStartTime = _offsetSeconds - chunkOverlap;

            // Build WAV in memory — avoids temp-file disk I/O on the hot path. wavBytes is the HTTP
            // payload and crosses into the channel/HTTP layer, so it stays a normal allocation.
            var ms = new MemoryStream(pcmLen + 64);
            using (var writer = new WaveFileWriter(ms, waveFormat))
                writer.Write(pcm, 0, pcmLen);
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
        finally
        {
            if (fresh is not null) ArrayPool<byte>.Shared.Return(fresh);
            if (pcm is not null) ArrayPool<byte>.Shared.Return(pcm);
        }
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
