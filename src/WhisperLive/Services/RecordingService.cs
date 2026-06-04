using NAudio.Wave;
using System.IO;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using WhisperLive.Models;

namespace WhisperLive.Services;

public sealed class RecordingService : IRecordingService
{
    private Channel<AudioChunkInfo> _channel = Channel.CreateBounded<AudioChunkInfo>(20);
    private WasapiLoopbackCapture? _capture;
    private MemoryStream _buffer = new();
    private readonly object _lock = new();
    private int _chunkIndex;
    private double _offsetSeconds;

    public ChannelReader<AudioChunkInfo> Chunks => _channel.Reader;

    public Task StartAsync(RecordingOptions options, CancellationToken ct)
    {
        _channel = Channel.CreateBounded<AudioChunkInfo>(
            new BoundedChannelOptions(20) { FullMode = BoundedChannelFullMode.Wait });
        _chunkIndex = 0;
        _offsetSeconds = 0;
        _buffer = new MemoryStream();

        _capture = new WasapiLoopbackCapture();
        var waveFormat = _capture.WaveFormat;

        _capture.DataAvailable += (_, e) =>
        {
            if (e.BytesRecorded == 0) return;
            lock (_lock)
                _buffer.Write(e.Buffer, 0, e.BytesRecorded);
        };

        _capture.StartRecording();
        _ = RunFlushLoopAsync(waveFormat, options, ct);
        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        _capture?.StopRecording();
        _capture?.Dispose();
        _capture = null;
        return Task.CompletedTask;
    }

    private async Task RunFlushLoopAsync(WaveFormat waveFormat, RecordingOptions options, CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(options.ChunkDurationSeconds));
        try
        {
            while (await timer.WaitForNextTickAsync(ct))
                await FlushAsync(waveFormat, options);
        }
        catch (OperationCanceledException) { }

        await FlushAsync(waveFormat, options);
        _channel.Writer.TryComplete();
    }

    private async Task FlushAsync(WaveFormat waveFormat, RecordingOptions options)
    {
        byte[] data;
        lock (_lock)
        {
            if (_buffer.Length == 0) return;
            data = _buffer.ToArray();
            _buffer.SetLength(0);
            _buffer.Position = 0;
        }

        var path = Path.Combine(Path.GetTempPath(), $"whisper_{_chunkIndex:D4}.wav");
        using (var writer = new WaveFileWriter(path, waveFormat))
            writer.Write(data, 0, data.Length);

        await _channel.Writer.WriteAsync(new AudioChunkInfo(path, _chunkIndex, _offsetSeconds));
        _offsetSeconds += options.ChunkDurationSeconds;
        _chunkIndex++;
    }

    public void Dispose()
    {
        _capture?.Dispose();
        _buffer.Dispose();
    }
}
