using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using WhisperLive.Infrastructure;
using WhisperLive.Models;
using WhisperLive.Services.Translation.Providers;

namespace WhisperLive.Services.Translation;

/// <summary>
/// Background translation pipeline. Always active when instantiated.
/// For the disabled case, the factory returns DisabledTranslationService instead.
///
/// - Unbounded channel: no segment is ever dropped, regardless of API speed.
/// - SemaphoreSlim(3): limits concurrent API calls to prevent rate-limit cascades.
/// - On translation complete, fires SegmentTranslated for UI updates.
/// </summary>
public sealed class TranslationService : ITranslationService, IDisposable
{
    private const int MaxConcurrent = 3;
    private const int MaxRetries = 2;

    private readonly ITranslationProvider _provider;
    private readonly SemaphoreSlim _concurrencySemaphore = new(MaxConcurrent, MaxConcurrent);

    // Every segment queued is eventually translated — no eviction under load.
    private readonly Channel<SubtitleSegment> _channel = Channel.CreateUnbounded<SubtitleSegment>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });

    private CancellationTokenSource? _sessionCts;
    private Task? _workerTask;

    public string TargetLanguage { get; }
    public bool IsEnabled => true;

    public event EventHandler<SegmentTranslationReadyEventArgs>? SegmentTranslated;

    public TranslationService(ITranslationProvider provider, string targetLanguage)
    {
        _provider = provider;
        TargetLanguage = targetLanguage;
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    public void StartSession()
    {
        EndSession();
        // Drain any items left in the channel from a previous session.
        // Without this, stale segments would be consumed by this session's worker.
        while (_channel.Reader.TryRead(out _)) { }

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

        _workerTask?.Wait(TimeSpan.FromSeconds(1));
        _workerTask = null;

        AppLogger.Info("TranslationService session ended");
    }

    // ── Enqueue ───────────────────────────────────────────────────────────────

    public void EnqueueSegment(SubtitleSegment segment) => _channel.Writer.TryWrite(segment);

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
                SegmentTranslated?.Invoke(this, new SegmentTranslationReadyEventArgs(segment.Id, translated));
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

    // ── IDisposable ───────────────────────────────────────────────────────────

    public void Dispose()
    {
        EndSession();
        _concurrencySemaphore.Dispose();
    }
}
