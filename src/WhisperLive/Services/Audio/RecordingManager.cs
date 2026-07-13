using System;
using System.Collections.Generic;
using System.Diagnostics;
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
    // Only 1 concurrent transcription in-flight. asyncio.Lock on the server
    // serialises requests anyway — having more than 1 in-flight just queues stale
    // audio on the server (measured: semaphore(3) caused 20s latency chains).
    // Drop guard kicks in immediately when the single slot is occupied, so
    // ConsumeChunksAsync always re-reads the freshest chunk from the channel.
    private readonly SemaphoreSlim _inflightSemaphore = new(1, 1);
    private int _consecutiveFailures;

    public event EventHandler? ApiStalled;

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
        Interlocked.Exchange(ref _consecutiveFailures, 0);
        PipelineMetrics.Instance.Reset();
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

        // Drain the single inflight slot — unblocks only when FireChunkAsync has released,
        // guaranteeing no more writes to _subtitle after EndSession(). Bounded: if an
        // in-flight transcription doesn't honour cancellation within 5s (e.g. a socket
        // stuck before its own timeout fires), stop anyway rather than hang the caller.
        if (await _inflightSemaphore.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false))
            _inflightSemaphore.Release(1);
        else
            AppLogger.Warning("Stop drain timed out — in-flight transcription didn't release in 5s");

        _subtitle.SegmentAdded -= OnSubtitleSegmentAdded;
        _subtitle.EndSession();
        _cts = null;

        PipelineMetrics.Instance.LogSummary();
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
                    current = newer;
                }

                // Drop chunk if both inflight slots are already occupied.
                // When chunk rate > API throughput (e.g. 2s chunks on slow CPU), queuing tasks on
                // the semaphore means they wait minutes before running — transcript freezes on stale
                // audio. Dropping here keeps the two in-flight tasks processing the freshest audio.
                if (_inflightSemaphore.CurrentCount == 0)
                {
                    AppLogger.Warning("Chunk #{Index} dropped — API saturated (chunk rate > throughput)", current.ChunkIndex);
                    continue;
                }

                _ = FireChunkAsync(current, options, ct);
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            // In-memory chunks: no files to clean up, just drain the channel.
            while (_recording.Chunks.TryRead(out _)) { }
        }
    }

    private async Task FireChunkAsync(AudioChunkInfo chunk, RecordingOptions options, CancellationToken ct)
    {
        try { await _inflightSemaphore.WaitAsync(ct); }
        catch (OperationCanceledException) { return; }

        var success = false;
        try
        {
            for (var attempt = 0; attempt < 2; attempt++)
            {
                if (attempt > 0)
                {
                    try { await Task.Delay(2_000, ct); }
                    catch (OperationCanceledException) { return; }
                }
                try
                {
                    var bytes = chunk.WavData.Length;
                    var sw = Stopwatch.StartNew();
                    var segs = await _transcription.TranscribeChunkAsync(chunk, BuildChunkOptions(options), ct);
                    sw.Stop();
                    if (!ct.IsCancellationRequested)
                    {
                        _subtitle.AppendSegments(segs);
                        PipelineMetrics.Instance.RecordTranscription(sw.Elapsed.TotalMilliseconds, bytes, segs.Count());
                    }
                    success = true;
                    Interlocked.Exchange(ref _consecutiveFailures, 0);
                    return;
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
                catch (Exception ex)
                {
                    AppLogger.Warning(ex,
                        attempt == 0
                            ? "Chunk #{Index} failed — retrying in 2s"
                            : "Chunk #{Index} failed after retry — skipping",
                        chunk.ChunkIndex);
                }
            }
        }
        finally
        {
            _inflightSemaphore.Release();
        }

        if (!success)
        {
            var count = Interlocked.Increment(ref _consecutiveFailures);
            if (count >= 3) ApiStalled?.Invoke(this, EventArgs.Empty);
        }
    }

    // Injects last ~100 transcript words as initial_prompt for vocabulary continuity.
    private RecordingOptions BuildChunkOptions(RecordingOptions options)
    {
        // Snapshot under lock, then do LINQ + string.Join outside so concurrent
        // FireChunkAsync threads don't serialize during the string-split work.
        List<string> snapshot;
        lock (_segLock) snapshot = [.._recentSegments];
        var prompt = string.Join(" ",
            snapshot.SelectMany(s => s.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                    .TakeLast(100));
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
        _inflightSemaphore.Dispose();
    }
}
