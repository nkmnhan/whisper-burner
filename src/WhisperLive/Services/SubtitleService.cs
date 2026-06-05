using System;
using System.Collections.Generic;
using System.IO;
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
    private StreamWriter? _writer;

    public event EventHandler<SubtitleSegment>? SegmentAdded;

    public IReadOnlyList<SubtitleSegment> AllSegments => _segments;
    public string? CurrentSessionPath { get; private set; }

    public void StartSession()
    {
        EndSession();
        _segments.Clear();

        Directory.CreateDirectory(_sessionsDir);
        CurrentSessionPath = Path.Combine(
            _sessionsDir,
            $"{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.srt");

        _writer = new StreamWriter(CurrentSessionPath, append: false, Encoding.UTF8) { AutoFlush = true };
        AppLogger.Info("Session file opened: {Path}", CurrentSessionPath);
    }

    public void EndSession()
    {
        _writer?.Flush();
        _writer?.Dispose();
        _writer = null;
        if (CurrentSessionPath is not null)
            AppLogger.Info("Session file closed: {Path}", CurrentSessionPath);
    }

    public void AppendSegments(IEnumerable<SubtitleSegment> segments)
    {
        foreach (var seg in segments)
        {
            // Reassign Id to be globally sequential across all chunks
            var globalSeg = seg with { Id = _segments.Count + 1 };
            _segments.Add(globalSeg);
            WriteSrtEntry(globalSeg);
            SegmentAdded?.Invoke(this, globalSeg);
        }
    }

    private void WriteSrtEntry(SubtitleSegment seg)
    {
        if (_writer is null) return;
        try { _writer.Write(seg.ToSrtEntry()); _writer.WriteLine(); }
        catch (Exception ex) { AppLogger.Warning(ex, "Failed to write segment to session file"); }
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
