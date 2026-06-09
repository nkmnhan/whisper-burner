using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WhisperLive.Infrastructure;
using WhisperLive.Models;
using WhisperLive.Services.Audio;

namespace WhisperLive.Services.Assistant;

public sealed class TranscriptCorrectionService : ITranscriptCorrectionService, IDisposable
{
    private static readonly TimeSpan CorrectionInterval = TimeSpan.FromMinutes(3);

    private const string CorrectionPromptTemplate =
        "Fix ONLY these issues in each transcript line:\n" +
        "1. Trailing \"...\" indicating a clipped word — complete the word using context\n" +
        "2. Trailing \"-\" indicating a word split at a chunk boundary — complete the word\n" +
        "3. Leading words duplicated from the previous line — remove the duplicates\n\n" +
        "Return ONLY a JSON array of corrected strings, one per input line, same order.\n" +
        "Do not rephrase, add meaning, or alter correct text.\n\n" +
        "Input:\n";

    private readonly IRecordingManager _recordingManager;
    private readonly ISubtitleService _subtitleService;
    private readonly IAiProvider _aiProvider;
    private readonly object _bufferLock = new();
    private readonly List<SubtitleSegment> _buffer = [];
    private readonly SemaphoreSlim _flushLock = new(1, 1);

    private CancellationTokenSource? _sessionCts;

    public event EventHandler<IReadOnlyList<CorrectedSegment>>? BatchCorrected;

    public TranscriptCorrectionService(
        IRecordingManager recordingManager,
        ISubtitleService subtitleService,
        IAiProvider aiProvider)
    {
        _recordingManager = recordingManager;
        _subtitleService = subtitleService;
        _aiProvider = aiProvider;
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    public void StartSession()
    {
        lock (_bufferLock) _buffer.Clear();
        _sessionCts = new CancellationTokenSource();
        _recordingManager.SegmentAdded += OnSegmentAdded;
        _ = RunPeriodicCorrectionAsync(_sessionCts.Token);
    }

    public void EndSession()
    {
        _recordingManager.SegmentAdded -= OnSegmentAdded;
        _sessionCts?.Cancel();
        _sessionCts?.Dispose();
        _sessionCts = null;
        _ = FlushAsync(CancellationToken.None);
    }

    private void OnSegmentAdded(object? sender, SubtitleSegment seg)
    {
        lock (_bufferLock) _buffer.Add(seg);
    }

    private async Task RunPeriodicCorrectionAsync(CancellationToken ct)
    {
        try
        {
            using var timer = new PeriodicTimer(CorrectionInterval);
            while (await timer.WaitForNextTickAsync(ct))
                await FlushAsync(ct);
        }
        catch (OperationCanceledException) { }
    }

    // ── Flush ─────────────────────────────────────────────────────────────────

    public async Task FlushAsync(CancellationToken ct = default)
    {
        List<SubtitleSegment> batch;
        lock (_bufferLock)
        {
            if (_buffer.Count == 0) return;
            batch = new List<SubtitleSegment>(_buffer);
            _buffer.Clear();
        }

        await _flushLock.WaitAsync(ct);
        try
        {
            var corrected = await CorrectBatchAsync(batch, ct);
            if (corrected.Count == 0) return;
            _subtitleService.ApplyCorrections(corrected);
            BatchCorrected?.Invoke(this, corrected);
        }
        catch (Exception ex)
        {
            AppLogger.Warning(ex, "Transcript correction batch failed — raw transcript unaffected");
        }
        finally
        {
            _flushLock.Release();
        }
    }

    private async Task<IReadOnlyList<CorrectedSegment>> CorrectBatchAsync(
        List<SubtitleSegment> batch, CancellationToken ct)
    {
        var inputJson = JsonSerializer.Serialize(batch.Select(s => s.Text).ToList());
        var prompt = CorrectionPromptTemplate + inputJson;

        var responseText = await _aiProvider.CompleteAsync(prompt, ct);

        var trimmed = responseText.Trim();
        if (trimmed.StartsWith("```"))
        {
            var firstNewline = trimmed.IndexOf('\n');
            var lastFence = trimmed.LastIndexOf("```");
            if (firstNewline >= 0 && lastFence > firstNewline)
                trimmed = trimmed[(firstNewline + 1)..lastFence].Trim();
        }

        using var arrayDoc = JsonDocument.Parse(trimmed);
        var corrected = new List<CorrectedSegment>();
        var i = 0;
        foreach (var item in arrayDoc.RootElement.EnumerateArray())
        {
            if (i < batch.Count)
                corrected.Add(new CorrectedSegment(batch[i].Id, item.GetString() ?? batch[i].Text));
            i++;
        }
        if (i != batch.Count)
            AppLogger.Warning("Correction batch count mismatch: sent {Sent}, received {Received}", batch.Count, i);
        return corrected;
    }

    public void Dispose()
    {
        _sessionCts?.Cancel();
        _sessionCts?.Dispose();
        _flushLock.Dispose();
    }
}
