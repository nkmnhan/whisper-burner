using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using WhisperLive.Models;

namespace WhisperLive.Services;

public sealed class SubtitleService : ISubtitleService
{
    private readonly List<SubtitleSegment> _segments = [];

    public event EventHandler<SubtitleSegment>? SegmentAdded;

    public IReadOnlyList<SubtitleSegment> AllSegments => _segments;

    public void AppendSegments(IEnumerable<SubtitleSegment> segments)
    {
        foreach (var seg in segments)
        {
            _segments.Add(seg);
            SegmentAdded?.Invoke(this, seg);
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
}
