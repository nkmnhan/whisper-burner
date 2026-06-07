using NAudio.Wave;
using System.IO;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using WhisperLive.Infrastructure;
using WhisperLive.Models;

namespace WhisperLive.Services;

public sealed class RecordingService : IRecordingService
{
    private const double OverlapSeconds = 0.3; // tail of previous chunk prepended to next

    private Channel<AudioChunkInfo> _channel = Channel.CreateBounded<AudioChunkInfo>(20);
    private WasapiLoopbackCapture? _capture;
    private MemoryStream _buffer = new();
    private readonly object _lock = new();
    private int _chunkIndex;
    private double _offsetSeconds;
    private byte[] _overlapTail = [];

    private volatile bool _paused;
    public bool IsPaused => _paused;

    public ChannelReader<AudioChunkInfo> Chunks => _channel.Reader;

    public Task StartAsync(RecordingOptions options, CancellationToken ct)
    {
        _channel = Channel.CreateBounded<AudioChunkInfo>(
            new BoundedChannelOptions(5) { FullMode = BoundedChannelFullMode.DropOldest });
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
                FlushChunk(waveFormat, options);  // sync — never blocks the timer
        }
        catch (OperationCanceledException) { }

        FlushChunk(waveFormat, options);
        _channel.Writer.TryComplete();
    }

    // Sync so the PeriodicTimer loop can never be stalled by channel backpressure.
    // TryWrite with DropOldest drops the oldest queued chunk when full, keeping
    // the channel current without blocking this method.
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

        // Prepend overlap tail from previous chunk so boundary words are fully captured
        double chunkOverlap = _overlapTail.Length > 0 ? OverlapSeconds : 0.0;
        byte[] wavData;
        if (_overlapTail.Length > 0)
        {
            wavData = new byte[_overlapTail.Length + freshData.Length];
            _overlapTail.CopyTo(wavData, 0);
            freshData.CopyTo(wavData, _overlapTail.Length);
        }
        else
        {
            wavData = freshData;
        }

        // Save new overlap tail from the END of the fresh (non-overlap) data
        int overlapBytes = (int)(OverlapSeconds * waveFormat.AverageBytesPerSecond);
        _overlapTail = freshData.Length >= overlapBytes
            ? freshData[^overlapBytes..]
            : freshData;

        // OffsetSeconds = actual wall-clock start of this WAV (overlap region begins here)
        double wavStartTime = _offsetSeconds - chunkOverlap;

        var path = Path.Combine(Path.GetTempPath(), $"whisper_{_chunkIndex:D4}.wav");
        using (var writer = new WaveFileWriter(path, waveFormat))
            writer.Write(wavData, 0, wavData.Length);

        AppLogger.Debug("Flushed chunk #{Index} — {Bytes} bytes (overlap={Overlap}s) → {Path}",
            _chunkIndex, wavData.Length, chunkOverlap, path);
        _channel.Writer.TryWrite(new AudioChunkInfo(path, _chunkIndex, wavStartTime, chunkOverlap));
        _offsetSeconds += options.ChunkDurationSeconds;
        _chunkIndex++;
    }

    public void Dispose()
    {
        _capture?.Dispose();
        _buffer.Dispose();
    }
}
