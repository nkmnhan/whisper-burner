using NAudio.Wave;
using System.Threading.Channels;
using WhisperBurner.WinUI.Infrastructure;
using WhisperBurner.WinUI.Models;

namespace WhisperBurner.WinUI.Services;

public class RecordingService : IRecordingService
{
    private IWaveIn? _capture;
    private MemoryStream _buffer = new();
    private long _chunkSizeBytes;
    private WaveFormat? _captureFormat;
    private readonly object _lock = new();

    // Bounded channel: if transcription falls behind, oldest chunks are dropped
    // to keep subtitles close to real-time rather than accumulating lag
    private Channel<string>? _channel;
    private Task? _consumerTask;

    public bool IsRecording { get; private set; }
    public event EventHandler<string>? AudioChunkReady;

    public Task StartAsync(CaptureRegion region, RecordingOptions options, string outputMp4Path)
    {
        Directory.CreateDirectory(AppSettings.TempRoot);

        _capture = AppSettings.Current.CaptureSystemAudio
            ? new WasapiLoopbackCapture()
            : new WaveInEvent { WaveFormat = new WaveFormat(16000, 16, 1) };

        _captureFormat = _capture.WaveFormat;
        _chunkSizeBytes = (long)(_captureFormat.AverageBytesPerSecond
            * AppSettings.Current.ChunkDurationSeconds);

        AppLogger.Info($"Audio capture: {_capture.GetType().Name} | " +
                       $"format={_captureFormat.SampleRate}Hz {_captureFormat.BitsPerSample}bit " +
                       $"{_captureFormat.Channels}ch {_captureFormat.Encoding} | " +
                       $"chunkSize={_chunkSizeBytes:N0} bytes ({AppSettings.Current.ChunkDurationSeconds}s)");

        _channel = Channel.CreateBounded<string>(new BoundedChannelOptions(5)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true
        });

        _buffer = new MemoryStream();
        _dataEventCount = 0;
        _capture.DataAvailable += OnDataAvailable;
        _capture.StartRecording();
        IsRecording = true;

        _consumerTask = Task.Run(ConsumeChunksAsync);
        AppLogger.Info("Recording started — consumer task running");
        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        if (_capture == null) return;
        _capture.StopRecording();
        _capture.DataAvailable -= OnDataAvailable;
        _capture.Dispose();
        _capture = null;
        IsRecording = false;

        byte[] remaining;
        lock (_lock) { remaining = _buffer.ToArray(); _buffer.SetLength(0); }
        if (remaining.Length > 0 && _captureFormat != null)
            FlushChunk(remaining);

        // Complete channel and wait for consumer to drain remaining queued chunks
        _channel?.Writer.TryComplete();
        if (_consumerTask != null)
        {
            await _consumerTask;
            _consumerTask = null;
        }
        _channel = null;

        CleanupTempFiles();
    }

    private int _dataEventCount;
    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        _dataEventCount++;
        if (_dataEventCount == 1 || _dataEventCount % 20 == 0)
            AppLogger.Info($"DataAvailable #{_dataEventCount}: {e.BytesRecorded} bytes, " +
                           $"buffer={_buffer.Length:N0}/{_chunkSizeBytes:N0}");

        byte[]? chunk = null;
        lock (_lock)
        {
            _buffer.Write(e.Buffer, 0, e.BytesRecorded);
            if (_buffer.Length >= _chunkSizeBytes)
            {
                chunk = _buffer.ToArray();
                _buffer.SetLength(0);
            }
        }
        if (chunk != null) FlushChunk(chunk);
    }

    private void FlushChunk(byte[] pcm)
    {
        if (_captureFormat == null || _channel == null) return;
        var path = Path.Combine(AppSettings.TempRoot, $"chunk_{Guid.NewGuid():N}.wav");
        using (var writer = new WaveFileWriter(path, _captureFormat))
            writer.Write(pcm, 0, pcm.Length);

        if (!_channel.Writer.TryWrite(path))
            AppLogger.Warn($"Channel full — chunk dropped to prevent lag: {Path.GetFileName(path)}");
    }

    private async Task ConsumeChunksAsync()
    {
        if (_channel == null) return;
        await foreach (var path in _channel.Reader.ReadAllAsync())
            AudioChunkReady?.Invoke(this, path);
        AppLogger.Info("Consumer task completed");
    }

    private static void CleanupTempFiles()
    {
        try
        {
            if (Directory.Exists(AppSettings.TempRoot))
                foreach (var f in Directory.GetFiles(AppSettings.TempRoot, "chunk_*.wav"))
                    File.Delete(f);
        }
        catch { }
    }
}
