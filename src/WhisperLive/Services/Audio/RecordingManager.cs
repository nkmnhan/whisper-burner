using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using WhisperLive.Infrastructure;
using WhisperLive.Models;

namespace WhisperLive.Services.Audio;

/// <summary>
/// App-level singleton. Owns the full recording lifecycle so page navigation
/// never interrupts a session. One session at a time is enforced.
/// </summary>
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

        // Wait for the consumer loop to drain the last chunk BEFORE unsubscribing
        // SegmentAdded — ensures every segment from the final audio chunk fires the
        // handler (and reaches TranslationService.EnqueueSegment) before we close.
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
            if (_recentSegments.Count > 500)
                _recentSegments.RemoveAt(0);
        }
        SegmentAdded?.Invoke(this, seg);
    }

    private async Task ConsumeChunksAsync(RecordingOptions options, CancellationToken ct)
    {
        await foreach (var chunk in _recording.Chunks.ReadAllAsync(ct))
        {
            try
            {
                var segments = await _transcription.TranscribeChunkAsync(chunk, options, ct);
                _subtitle.AppendSegments(segments);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { AppLogger.Error(ex, "Transcription error on chunk"); }
            finally { try { File.Delete(chunk.FilePath); } catch { } }
        }
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
        _consumeTask = null;
    }
}
