using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WhisperLive.Infrastructure;
using WhisperLive.Models;

namespace WhisperLive.Services.Audio;

public sealed class RecordingManager : IRecordingManager, IDisposable
{
    private readonly IRecordingService _recording;
    private readonly ITranscriptionClient _transcription;
    private readonly ISubtitleService _subtitle;

    private CancellationTokenSource? _cts;
    private Task? _consumeTask;
    private readonly object _segLock = new();
    private readonly List<string> _recentSegments = [];

    public RecordingState State { get; private set; } = RecordingState.Idle;
    public string? CurrentSessionPath => _subtitle.CurrentSessionPath;

    public event EventHandler<RecordingState>? StateChanged;
    public event EventHandler<SubtitleSegment>? SegmentAdded;

    public RecordingManager(
        IRecordingService recording,
        ITranscriptionClient transcription,
        ISubtitleService subtitle)
    {
        _recording = recording;
        _transcription = transcription;
        _subtitle = subtitle;
    }

    public IReadOnlyList<string> GetRecentSegments()
    {
        lock (_segLock) return new List<string>(_recentSegments);
    }

    public Task StartAsync(RecordingOptions options)
    {
        if (State != RecordingState.Idle) return Task.CompletedTask;

        lock (_segLock) _recentSegments.Clear();
        _cts = new CancellationTokenSource();
        _transcription.ResetPrompt();
        _subtitle.StartSession();
        _subtitle.SegmentAdded += OnSubtitleSegmentAdded;
        _ = _recording.StartAsync(options, _cts.Token);
        _consumeTask = ConsumeChunksAsync(options, _cts.Token);

        SetState(RecordingState.Recording);
        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        if (State == RecordingState.Idle) return;

        _cts?.Cancel();
        await _recording.StopAsync();

        if (_consumeTask is { } t)
        {
            try { await t.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
            catch (Exception ex) { AppLogger.Warning(ex, "ConsumeChunksAsync faulted on stop"); }
        }
        _consumeTask = null;

        _subtitle.SegmentAdded -= OnSubtitleSegmentAdded;
        _subtitle.EndSession();
        _cts = null;

        SetState(RecordingState.Idle);
    }

    public Task PauseAsync()
    {
        if (State != RecordingState.Recording) return Task.CompletedTask;
        _ = _recording.PauseAsync();
        SetState(RecordingState.Paused);
        return Task.CompletedTask;
    }

    public Task ResumeAsync()
    {
        if (State != RecordingState.Paused) return Task.CompletedTask;
        _ = _recording.ResumeAsync();
        SetState(RecordingState.Recording);
        return Task.CompletedTask;
    }

    private void OnSubtitleSegmentAdded(object? sender, SubtitleSegment seg)
    {
        lock (_segLock)
        {
            _recentSegments.Add(seg.Text);
            if (_recentSegments.Count > 20)
                _recentSegments.RemoveAt(0);
        }
        SegmentAdded?.Invoke(this, seg);
    }

    private async Task ConsumeChunksAsync(RecordingOptions options, CancellationToken ct)
    {
        try
        {
            await foreach (var chunk in _recording.Chunks.ReadAllAsync(ct))
            {
                // Skip stale chunks if the pipeline has fallen behind real-time.
                var current = chunk;
                while (_recording.Chunks.TryRead(out var newer))
                {
                    AppLogger.Warning("Skipping stale chunk #{Index} — pipeline behind", current.ChunkIndex);
                    try { File.Delete(current.FilePath); } catch { }
                    current = newer;
                }

                // Fire and forget — don't block the recording loop waiting for any individual chunk.
                // Each chunk lands in the transcript whenever its response arrives.
                _ = FireChunkAsync(current, options, ct);
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            while (_recording.Chunks.TryRead(out var leftover))
                try { File.Delete(leftover.FilePath); } catch { }
        }
    }

    private async Task FireChunkAsync(AudioChunkInfo chunk, RecordingOptions options, CancellationToken ct)
    {
        try
        {
            var segments = await _transcription.TranscribeChunkAsync(chunk, BuildChunkOptions(options), ct);
            if (!ct.IsCancellationRequested)
                _subtitle.AppendSegments(segments);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception ex)
        {
            AppLogger.Warning(ex, "Chunk #{Index} timed out or failed — skipping", chunk.ChunkIndex);
        }
        finally
        {
            try { File.Delete(chunk.FilePath); } catch { }
        }
    }

    // Injects last ~100 transcript words as initial_prompt for vocabulary continuity.
    private RecordingOptions BuildChunkOptions(RecordingOptions options)
    {
        string prompt;
        lock (_segLock)
        {
            prompt = string.Join(" ",
                _recentSegments
                    .SelectMany(s => s.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                    .TakeLast(100));
        }
        return string.IsNullOrEmpty(prompt) ? options : options with { InitialPrompt = prompt };
    }

    private void SetState(RecordingState state)
    {
        State = state;
        StateChanged?.Invoke(this, state);
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _subtitle.SegmentAdded -= OnSubtitleSegmentAdded;
        _consumeTask = null;
    }
}
