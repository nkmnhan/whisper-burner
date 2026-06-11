using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using WhisperLive.Infrastructure;
using WhisperLive.Models;
using WhisperLive.Services.Translation.Providers;

namespace WhisperLive.Services.Translation;

/// <summary>
/// Background translation pipeline.
///
/// Architecture (matches the EveryTongue "update/commit" pattern validated by research):
///   • New segment arrives → enqueued, UI shows immediately as italic/dimmed (Provisional)
///   • Bounded Channel (capacity=20, DropOldest) — if the API is slow, oldest pending
///     segments are dropped instead of blocking newer, more relevant ones
///   • SemaphoreSlim(3) — max 3 concurrent API calls; prevents rate-limit cascade
///   • On success → view.ApplyTranslation() fires INPC → in-place UI update (italic→normal)
///   • On failure → view.MarkFailed(); original text shown as fallback
/// </summary>
public sealed class TranslationService : ITranslationService, IDisposable
{
    private const int MaxConcurrent = 3;
    private const int MaxRetries = 2;

    private readonly ITranslationProvider _provider;
    private readonly SemaphoreSlim _concurrencySemaphore = new(MaxConcurrent, MaxConcurrent);

    // Bounded channel: DropOldest ensures the UI never stalls on queue backpressure.
    private readonly Channel<TranslatedSegmentView> _channel =
        Channel.CreateBounded<TranslatedSegmentView>(
            new BoundedChannelOptions(20)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = false,
            });

    // Session-scoped list for SRT export — populated as translations arrive.
    private readonly List<TranslatedSegmentView> _sessionViews = [];
    private readonly object _viewsLock = new();

    private CancellationTokenSource? _sessionCts;
    private Task? _workerTask;

    public bool IsEnabled { get; }
    public string TargetLanguage { get; }

    public event EventHandler<SegmentTranslationReadyEventArgs>? SegmentTranslated;

    public TranslationService(ITranslationProvider provider, bool isEnabled, string targetLanguage)
    {
        _provider = provider;
        IsEnabled = isEnabled;
        TargetLanguage = targetLanguage;
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    public void StartSession()
    {
        EndSession();
        lock (_viewsLock) _sessionViews.Clear();

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
        AppLogger.Info("TranslationService session ended");
    }

    // ── Enqueue ───────────────────────────────────────────────────────────────

    public void EnqueueSegment(TranslatedSegmentView view)
    {
        if (!IsEnabled) return;
        lock (_viewsLock) _sessionViews.Add(view);
        // TryWrite never blocks. If full, DropOldest removes the least-relevant pending item.
        _channel.Writer.TryWrite(view);
    }

    // ── Background consumer ───────────────────────────────────────────────────

    private async Task ConsumeAsync(CancellationToken ct)
    {
        await foreach (var view in _channel.Reader.ReadAllAsync(ct))
        {
            // Fire-and-forget each translation with bounded concurrency.
            _ = TranslateWithSemaphoreAsync(view, ct);
        }
    }

    private async Task TranslateWithSemaphoreAsync(TranslatedSegmentView view, CancellationToken ct)
    {
        await _concurrencySemaphore.WaitAsync(ct);
        try
        {
            await TranslateWithRetryAsync(view, ct);
        }
        catch (OperationCanceledException) { /* session ended — leave view as Provisional */ }
        catch (Exception ex)
        {
            AppLogger.Warning(ex, "Translation failed for segment {Id}", view.Original.Id);
            view.MarkFailed();
        }
        finally
        {
            _concurrencySemaphore.Release();
        }
    }

    private async Task TranslateWithRetryAsync(TranslatedSegmentView view, CancellationToken ct)
    {
        var delay = TimeSpan.FromSeconds(1);
        for (var attempt = 0; attempt <= MaxRetries; attempt++)
        {
            try
            {
                var translated = await _provider.TranslateAsync(view.OriginalText, TargetLanguage, ct);
                // Do NOT call view.ApplyTranslation here — INPC must fire on the UI thread.
                // Fire the event with translated text; the page handler dispatches and applies.
                SegmentTranslated?.Invoke(this, new SegmentTranslationReadyEventArgs(view, translated));
                return;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) when (attempt < MaxRetries)
            {
                AppLogger.Warning(ex, "Translation attempt {Attempt} failed, retrying in {Delay}ms",
                    attempt + 1, delay.TotalMilliseconds);
                await Task.Delay(delay + TimeSpan.FromMilliseconds(Random.Shared.Next(0, 500)), ct);
                delay = delay * 2; // exponential backoff
            }
        }
        // All retries exhausted — mark failed so original is shown cleanly.
        view.MarkFailed();
    }

    // ── SRT export ────────────────────────────────────────────────────────────

    public async Task WriteTranslatedSrtAsync(string originalSrtPath, CancellationToken ct = default)
    {
        List<TranslatedSegmentView> snapshot;
        lock (_viewsLock) snapshot = [.._sessionViews];

        var translated = snapshot
            .FindAll(v => v.State == TranslationSegmentState.Translated && v.TranslatedText is not null);

        if (translated.Count == 0) return;

        var sb = new StringBuilder();
        for (var i = 0; i < translated.Count; i++)
        {
            var v = translated[i];
            var seg = v.Original with { Id = i + 1, Text = v.TranslatedText! };
            sb.AppendLine(seg.ToSrtEntry());
        }

        // "session.srt" → "session.vi.srt"
        var ext = Path.GetExtension(originalSrtPath);
        var translatedPath = Path.ChangeExtension(originalSrtPath, $".{TargetLanguage}{ext}");
        await File.WriteAllTextAsync(translatedPath, sb.ToString(), Encoding.UTF8, ct);
        AppLogger.Info("Translated SRT written: {Path}", translatedPath);
    }

    // ── IDisposable ───────────────────────────────────────────────────────────

    public void Dispose()
    {
        EndSession();
        _concurrencySemaphore.Dispose();
    }
}
