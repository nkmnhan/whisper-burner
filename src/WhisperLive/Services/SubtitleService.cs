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
            _segments.Add(seg);
            WriteSrtEntry(seg);
            SegmentAdded?.Invoke(this, seg);
        }
    }

    private void WriteSrtEntry(SubtitleSegment seg)
    {
        if (_writer is null) return;
        try
        {
            _writer.WriteLine(seg.Id);
            _writer.WriteLine($"{FormatSrtTime(seg.Start)} --> {FormatSrtTime(seg.End)}");
            _writer.WriteLine(seg.Text);
            _writer.WriteLine();
        }
        catch (Exception ex)
        {
            AppLogger.Warning(ex, "Failed to write segment to session file");
        }
    }

    public async Task ExportSrtAsync(string path)
    {
        var sb = new StringBuilder();
        foreach (var seg in _segments)
        {
            sb.AppendLine(seg.Id.ToString());
            sb.AppendLine($"{FormatSrtTime(seg.Start)} --> {FormatSrtTime(seg.End)}");
            sb.AppendLine(seg.Text);
            sb.AppendLine();
        }
        await File.WriteAllTextAsync(path, sb.ToString(), Encoding.UTF8);
    }

    private static string FormatSrtTime(double seconds)
    {
        var ts = TimeSpan.FromSeconds(seconds);
        return $"{ts.Hours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2},{ts.Milliseconds:D3}";
    }

    public void Dispose() => EndSession();
}
