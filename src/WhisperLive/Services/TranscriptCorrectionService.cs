using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WhisperLive.Infrastructure;
using WhisperLive.Models;

namespace WhisperLive.Services;

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

    private readonly RecordingManager _recordingManager;
    private readonly SubtitleService _subtitleService;
    private readonly object _bufferLock = new();
    private readonly List<SubtitleSegment> _buffer = [];

    private CancellationTokenSource? _sessionCts;

    public event EventHandler<IReadOnlyList<CorrectedSegment>>? BatchCorrected;

    public TranscriptCorrectionService(RecordingManager recordingManager, SubtitleService subtitleService)
    {
        _recordingManager = recordingManager;
        _subtitleService = subtitleService;
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
    }

    // ── Claude subprocess ─────────────────────────────────────────────────────

    private static async Task<IReadOnlyList<CorrectedSegment>> CorrectBatchAsync(
        List<SubtitleSegment> batch, CancellationToken ct)
    {
        var inputJson = JsonSerializer.Serialize(batch.Select(s => s.Text).ToList());
        var prompt = CorrectionPromptTemplate + inputJson;

        var psi = new ProcessStartInfo
        {
            FileName = "claude",
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add("-p");
        psi.ArgumentList.Add("--output-format");
        psi.ArgumentList.Add("json");

        Process process;
        try
        {
            process = Process.Start(psi) ?? throw new InvalidOperationException("Process.Start returned null.");
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Claude Code CLI not found — is it installed and on PATH?", ex);
        }

        using (process)
        {
            await process.StandardInput.WriteAsync(prompt);
            process.StandardInput.Close();

            var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
            var stderrTask = process.StandardError.ReadToEndAsync(ct);
            await process.WaitForExitAsync(ct);
            var stdout = await stdoutTask;
            var stderr = await stderrTask;

            if (process.ExitCode != 0)
                throw new InvalidOperationException($"Claude exited with error: {stderr.Trim()}");

            using var doc = JsonDocument.Parse(stdout);
            var root = doc.RootElement;
            if (root.TryGetProperty("is_error", out var isError) && isError.GetBoolean())
                throw new InvalidOperationException("Claude reported an error for this correction request.");

            var resultJson = root.TryGetProperty("result", out var r) ? r.GetString() ?? "[]" : "[]";
            using var arrayDoc = JsonDocument.Parse(resultJson);

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
    }

    public void Dispose()
    {
        _sessionCts?.Cancel();
        _sessionCts?.Dispose();
    }
}
