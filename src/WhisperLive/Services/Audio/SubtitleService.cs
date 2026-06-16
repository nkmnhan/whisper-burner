using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WhisperLive.Infrastructure;
using WhisperLive.Models;

namespace WhisperLive.Services.Audio;

public sealed class SubtitleService : ISubtitleService, IDisposable
{
    private static readonly string _sessionsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "whisper.live", "sessions");

    private readonly List<SubtitleSegment> _segments = [];
    private readonly object _segLock = new();
    private ISrtSessionWriter? _srtWriter;

    public event EventHandler<SubtitleSegment>? SegmentAdded;

    public IReadOnlyList<SubtitleSegment> AllSegments => _segments;
    public string? CurrentSessionPath { get; private set; }
    public string SessionsDirectory => _sessionsDir;

    public void StartSession()
    {
        EndSession();
        _segments.Clear();
        CurrentSessionPath = null;
        // Pre-compute path at session-start time so the timestamp reflects when recording began,
        // not when the first audio chunk arrived. File is created lazily on first TryWrite.
        Directory.CreateDirectory(_sessionsDir);
        var sessionPath = Path.Combine(_sessionsDir, $"{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.srt");
        _srtWriter = new StreamingSrtWriter(() => sessionPath);
    }

    public void EndSession()
    {
        _srtWriter?.Dispose();
        _srtWriter = null;
        if (CurrentSessionPath is not null)
            AppLogger.Info("Session file closed: {Path}", CurrentSessionPath);
        CurrentSessionPath = null;
    }

    public void AppendSegments(IEnumerable<SubtitleSegment> segments)
    {
        foreach (var seg in segments)
        {
            SubtitleSegment globalSeg;
            lock (_segLock)
            {
                var deduped = _segments.Count > 0
                    ? seg with { Text = StripLeadingOverlap(_segments[^1].Text, seg.Text) }
                    : seg;
                if (deduped.Text.Length == 0) continue;
                globalSeg = deduped with { Id = _segments.Count + 1 };
                _segments.Add(globalSeg);
            }
            WriteSrtEntry(globalSeg);
            SegmentAdded?.Invoke(this, globalSeg);
        }
    }

    // Strips word-level prefix of `current` that overlaps with a suffix of `prev`.
    // Handles Whisper's chunk-boundary repetition (e.g. prev: "Make the most of."
    // curr: "Make the most of means use your time" → "means use your time").
    // Minimum 2-word overlap required to avoid false positives on short common phrases.
    private static string StripLeadingOverlap(string prev, string current)
    {
        const int MinOverlapWords = 2;

        var prevWords = NormalizeWords(prev);
        var currWords = NormalizeWords(current);

        var maxK = Math.Min(prevWords.Count - 1, currWords.Count);
        if (maxK < MinOverlapWords) return current;

        for (var k = maxK; k >= MinOverlapWords; k--)
        {
            var match = true;
            for (var i = 0; i < k; i++)
            {
                if (!string.Equals(prevWords[prevWords.Count - k + i], currWords[i], StringComparison.OrdinalIgnoreCase))
                {
                    match = false;
                    break;
                }
            }
            if (!match) continue;

            // Skip k raw words from current (preserving original casing/spacing after them)
            var raw = current.AsSpan().TrimStart();
            for (var i = 0; i < k && raw.Length > 0; i++)
            {
                var space = raw.IndexOf(' ');
                raw = space < 0 ? [] : raw[(space + 1)..].TrimStart();
            }
            return raw.ToString();
        }

        return current;
    }

    private static List<string> NormalizeWords(string text) =>
        text.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(w => w.TrimEnd('.', ',', '?', '!', ';', ':'))
            .Where(w => w.Length > 0)
            .ToList();

    private void WriteSrtEntry(SubtitleSegment seg)
    {
        if (_srtWriter?.TryWrite(seg) == true && CurrentSessionPath is null)
        {
            CurrentSessionPath = _srtWriter.CurrentPath;
            AppLogger.Info("Session file opened: {Path}", CurrentSessionPath!);
        }
    }

    public void ApplyCorrections(IReadOnlyList<CorrectedSegment> corrections)
    {
        if (CurrentSessionPath is null) return;

        List<SubtitleSegment> snapshot;
        lock (_segLock)
        {
            foreach (var correction in corrections.OrderBy(c => c.OriginalId))
            {
                var index = correction.OriginalId - 1;
                if (index < 0 || index >= _segments.Count) continue;
                _segments[index] = _segments[index] with { Text = correction.CorrectedText };
            }
            snapshot = [.._segments];
        }

        var correctedPath = Path.ChangeExtension(CurrentSessionPath, ".corrected.srt");
        WriteSrtFile(correctedPath, snapshot, "Failed to write corrected SRT");
    }

    private static void WriteSrtFile(string path, List<SubtitleSegment> segments, string errorMsg)
    {
        try
        {
            var sb = new StringBuilder();
            for (var i = 0; i < segments.Count; i++)
                sb.AppendLine((segments[i] with { Id = i + 1 }).ToSrtEntry());
            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
        }
        catch (Exception ex) { AppLogger.Warning(ex, errorMsg); }
    }

    public async Task ExportSrtAsync(string path)
    {
        List<SubtitleSegment> snapshot;
        lock (_segLock) snapshot = [.._segments];
        var sb = new StringBuilder();
        for (var i = 0; i < snapshot.Count; i++)
            sb.AppendLine((snapshot[i] with { Id = i + 1 }).ToSrtEntry());
        await File.WriteAllTextAsync(path, sb.ToString(), Encoding.UTF8);
    }

    public void Dispose() => EndSession();
}
