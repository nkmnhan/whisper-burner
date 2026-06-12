using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using WhisperLive.Infrastructure;
using WhisperLive.Models;
using WhisperLive.Services.Audio;
using WhisperLive.Services.Translation.Providers;

namespace WhisperLive.Services.Translation;

/// <summary>
/// Background translation pipeline. Always active when instantiated.
/// For the disabled case, the factory returns DisabledTranslationService instead.
///
/// - Unbounded channel: no segment is ever dropped, regardless of API speed.
/// - SemaphoreSlim(3): limits concurrent API calls to prevent rate-limit cascades.
/// - Ordered drain: translations arrive out of order; a write lock + sequential pointer
///   ensures the translated SRT file is always in valid SRT order.
/// - Streaming write: ISrtSessionWriter.TryWrite writes each translated entry to disk immediately.
/// - EndSession fallback: untranslated segments fall back to original text so the SRT is complete.
/// </summary>
public sealed class TranslationService : ITranslationService, IDisposable
{
    private const int MaxConcurrent = 3;
    private const int MaxRetries = 2;

    private readonly ITranslationProvider _provider;
    private readonly Func<string?> _getSessionPath;
    private readonly SemaphoreSlim _concurrencySemaphore = new(MaxConcurrent, MaxConcurrent);
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    // Every segment queued is eventually translated — no eviction under load.
    private readonly Channel<SubtitleSegment> _channel = Channel.CreateUnbounded<SubtitleSegment>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });

    // Original segments keyed by ID — needed for SRT timestamps.
    private readonly ConcurrentDictionary<int, SubtitleSegment> _originals = new();

    // Ordered drain: hold out-of-order completions until sequential write is possible.
    private readonly Dictionary<int, string> _completedTexts = new();
    private int _nextWriteId = 1;
    private int _maxEnqueuedId;

    private ISrtSessionWriter? _srtWriter;
    private CancellationTokenSource? _sessionCts;
    private Task? _workerTask;

    public string TargetLanguage { get; }
    public bool IsEnabled => true;

    public event EventHandler<SegmentTranslationReadyEventArgs>? SegmentTranslated;

    public TranslationService(
        ITranslationProvider provider,
        string targetLanguage,
        Func<string?> getSessionPath)
    {
        _provider = provider;
        TargetLanguage = targetLanguage;
        _getSessionPath = getSessionPath;
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    public void StartSession()
    {
        EndSession();
        _originals.Clear();
        // Drain any items left in the channel from a previous session.
        // Without this, stale segments with mismatched IDs would be consumed
        // by this session's worker, corrupting the ordered drain.
        while (_channel.Reader.TryRead(out _)) { }
        // Use _writeLock for consistency — same lock guards all _completedTexts access.
        _writeLock.Wait();
        try { _completedTexts.Clear(); }
        finally { _writeLock.Release(); }
        _nextWriteId = 1;
        _maxEnqueuedId = 0;

        // Path factory: derives translated SRT path from the base session path once it's available.
        // Returns null until SubtitleService creates the raw SRT (retried on every TryWrite call).
        _srtWriter = new StreamingSrtWriter(() =>
        {
            var p = _getSessionPath();
            return p is null ? null : Path.ChangeExtension(p, $".{TargetLanguage}.srt");
        });

        _sessionCts = new CancellationTokenSource();
        _workerTask = Task.Run(() => ConsumeAsync(_sessionCts.Token));
        AppLogger.Info("TranslationService session started (provider={Provider}, target={Lang})",
            _provider.Name, TargetLanguage);
    }

    public void EndSession()
    {
        if (_sessionCts is null) return;
        _sessionCts.Cancel();
        _sessionCts.Dispose();
        _sessionCts = null;

        // Wait for the consumer loop to exit.
        _workerTask?.Wait(TimeSpan.FromSeconds(1));
        _workerTask = null;

        // Drain the concurrency semaphore to confirm every fire-and-forget
        // TranslateWithSemaphoreAsync task has exited its try-finally and released
        // its slot. After cancellation they abort quickly (the HTTP call is also
        // cancelled). Without this wait, a task could still hold _writeLock inside
        // DrainCompletedEntries when FlushFallbacks() tries to acquire it —
        // causing the 5-second timeout to fire and losing fallback writes.
        for (var i = 0; i < MaxConcurrent; i++)
            _concurrencySemaphore.Wait(TimeSpan.FromSeconds(3));
        _concurrencySemaphore.Release(MaxConcurrent);

        FlushFallbacks();
        _srtWriter?.Dispose();
        _srtWriter = null;
        AppLogger.Info("TranslationService session ended");
    }

    // ── Enqueue ───────────────────────────────────────────────────────────────

    public void EnqueueSegment(SubtitleSegment segment)
    {
        _originals[segment.Id] = segment;
        Interlocked.Exchange(ref _maxEnqueuedId, Math.Max(_maxEnqueuedId, segment.Id));
        _channel.Writer.TryWrite(segment);
    }

    // ── Background consumer ───────────────────────────────────────────────────

    private async Task ConsumeAsync(CancellationToken ct)
    {
        await foreach (var segment in _channel.Reader.ReadAllAsync(ct))
            _ = TranslateWithSemaphoreAsync(segment, ct);
    }

    private async Task TranslateWithSemaphoreAsync(SubtitleSegment segment, CancellationToken ct)
    {
        await _concurrencySemaphore.WaitAsync(ct);
        try
        {
            await TranslateWithRetryAsync(segment, ct);
        }
        catch (OperationCanceledException) { /* session ended — EndSession writes fallback */ }
        catch (Exception ex)
        {
            AppLogger.Warning(ex, "Translation failed for segment {Id} after all retries", segment.Id);
            // Segment stays absent from _completedTexts; EndSession writes original-text fallback.
        }
        finally
        {
            _concurrencySemaphore.Release();
        }
    }

    private async Task TranslateWithRetryAsync(SubtitleSegment segment, CancellationToken ct)
    {
        var delay = TimeSpan.FromSeconds(1);
        for (var attempt = 0; attempt <= MaxRetries; attempt++)
        {
            try
            {
                var translated = await _provider.TranslateAsync(segment.Text, TargetLanguage, ct);
                await CommitTranslationAsync(segment.Id, translated, ct);
                return;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) when (attempt < MaxRetries)
            {
                AppLogger.Warning(ex, "Translation attempt {Attempt} failed, retrying in {Delay}ms",
                    attempt + 1, delay.TotalMilliseconds);
                await Task.Delay(delay + TimeSpan.FromMilliseconds(Random.Shared.Next(0, 500)), ct);
                delay = delay * 2;
            }
        }
    }

    // ── Ordered streaming write ───────────────────────────────────────────────

    private async Task CommitTranslationAsync(int segmentId, string translatedText, CancellationToken ct)
    {
        await _writeLock.WaitAsync(ct);
        try
        {
            _completedTexts[segmentId] = translatedText;
            DrainCompletedEntries();
        }
        finally
        {
            _writeLock.Release();
        }

        SegmentTranslated?.Invoke(this, new SegmentTranslationReadyEventArgs(segmentId, translatedText));
    }

    // Must be called under _writeLock.
    private void DrainCompletedEntries()
    {
        while (_completedTexts.TryGetValue(_nextWriteId, out var text)
               && _originals.TryGetValue(_nextWriteId, out var original))
        {
            WriteEntry(original, text);
            _completedTexts.Remove(_nextWriteId);
            _nextWriteId++;
        }
    }

    private void WriteEntry(SubtitleSegment original, string translatedText) =>
        // Caller owns text transformation: pass original timestamps, override text only.
        _srtWriter?.TryWrite(original with { Text = translatedText });

    // ── EndSession fallback ───────────────────────────────────────────────────

    private void FlushFallbacks()
    {
        // By this point EndSession() has already drained _concurrencySemaphore,
        // so no task should be holding _writeLock. The timeout here is a last-resort guard.
        if (!_writeLock.Wait(TimeSpan.FromSeconds(15)))
        {
            AppLogger.Warning("FlushFallbacks timed out waiting for write lock — some entries may be missing from the translated SRT");
            return;
        }
        try
        {
            // Any segment between _nextWriteId and _maxEnqueuedId that has no translation
            // gets its original text written so the translated SRT file is always complete.
            for (var id = _nextWriteId; id <= _maxEnqueuedId; id++)
            {
                if (!_completedTexts.ContainsKey(id) && _originals.TryGetValue(id, out var seg))
                    _completedTexts[id] = seg.Text;
            }
            DrainCompletedEntries();
        }
        finally
        {
            _writeLock.Release();
        }
    }

    // ── IDisposable ───────────────────────────────────────────────────────────

    public void Dispose()
    {
        EndSession();
        _concurrencySemaphore.Dispose();
        _writeLock.Dispose();
    }
}
