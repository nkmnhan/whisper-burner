using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WhisperLive.Infrastructure;
using WhisperLive.Models;

namespace WhisperLive.Services;

public sealed class SubtitleService : ISubtitleService, IDisposable
{
    private static readonly string _sessionsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "whisper.live", "sessions");

    private readonly List<SubtitleSegment> _segments = [];
    private readonly object _segLock = new();
    private StreamWriter? _writer;
    private StreamWriter? _correctedWriter;
    private bool _sessionPending;

    public event EventHandler<SubtitleSegment>? SegmentAdded;

    public IReadOnlyList<SubtitleSegment> AllSegments => _segments;
    public string? CurrentSessionPath { get; private set; }
    public string SessionsDirectory => _sessionsDir;

    public void StartSession()
    {
        EndSession();
        _segments.Clear();
        CurrentSessionPath = null;

        // Defer creating the .srt file until the first segment actually arrives —
        // starting (or restarting) a session that never produces a segment must not
        // leave an empty orphaned file behind.
        _sessionPending = true;
    }

    public void EndSession()
    {
        _writer?.Flush();
        _writer?.Dispose();
        _writer = null;
        _correctedWriter?.Flush();
        _correctedWriter?.Dispose();
        _correctedWriter = null;
        if (CurrentSessionPath is not null)
            AppLogger.Info("Session file closed: {Path}", CurrentSessionPath);
        _sessionPending = false;
    }

    public void AppendSegments(IEnumerable<SubtitleSegment> segments)
    {
        foreach (var seg in segments)
        {
            // Reassign Id to be globally sequential across all chunks
            SubtitleSegment globalSeg;
            lock (_segLock)
            {
                globalSeg = seg with { Id = _segments.Count + 1 };
                _segments.Add(globalSeg);
            }
            WriteSrtEntry(globalSeg);
            SegmentAdded?.Invoke(this, globalSeg);
        }
    }

    private void WriteSrtEntry(SubtitleSegment seg)
    {
        EnsureSessionFile();
        if (_writer is null) return;
        try { _writer.Write(seg.ToSrtEntry()); _writer.WriteLine(); }
        catch (Exception ex) { AppLogger.Warning(ex, "Failed to write segment to session file"); }
    }

    private void EnsureCorrectedFile()
    {
        if (_correctedWriter is not null || CurrentSessionPath is null) return;
        var correctedPath = Path.ChangeExtension(CurrentSessionPath, ".corrected.srt");
        _correctedWriter = new StreamWriter(correctedPath, append: false, Encoding.UTF8) { AutoFlush = true };
        AppLogger.Info("Corrected session file opened: {Path}", correctedPath);
    }

    private void EnsureSessionFile()
    {
        if (_writer is not null || !_sessionPending) return;

        Directory.CreateDirectory(_sessionsDir);
        CurrentSessionPath = Path.Combine(
            _sessionsDir,
            $"{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.srt");

        _writer = new StreamWriter(CurrentSessionPath, append: false, Encoding.UTF8) { AutoFlush = true };
        _sessionPending = false;
        AppLogger.Info("Session file opened: {Path}", CurrentSessionPath);
    }

    public void ApplyCorrections(IReadOnlyList<CorrectedSegment> corrections)
    {
        EnsureCorrectedFile();
        if (_correctedWriter is null) return;

        // Sort by Id so the .corrected.srt file has valid sequential SRT ordering
        foreach (var correction in corrections.OrderBy(c => c.OriginalId))
        {
            SubtitleSegment updated;
            lock (_segLock)
            {
                var index = correction.OriginalId - 1; // IDs are 1-based sequential
                if (index < 0 || index >= _segments.Count) continue;
                updated = _segments[index] with { Text = correction.CorrectedText };
                _segments[index] = updated;
            }
            try
            {
                _correctedWriter.Write(updated.ToSrtEntry());
                _correctedWriter.WriteLine();
            }
            catch (Exception ex) { AppLogger.Warning(ex, "Failed to write corrected segment"); }
        }
    }

    public async Task ExportSrtAsync(string path)
    {
        var sb = new StringBuilder();
        foreach (var seg in _segments)
            sb.AppendLine(seg.ToSrtEntry());
        await File.WriteAllTextAsync(path, sb.ToString(), Encoding.UTF8);
    }

    public void Dispose() => EndSession();
}
