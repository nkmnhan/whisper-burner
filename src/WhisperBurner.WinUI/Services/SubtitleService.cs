using WhisperBurner.WinUI.Models;

namespace WhisperBurner.WinUI.Services;

public class SubtitleService : ISubtitleService
{
    private readonly List<SubtitleSegment> _segments = [];
    private readonly object _lock = new();

    public IReadOnlyList<SubtitleSegment> Segments { get { lock (_lock) return [.. _segments]; } }
    public event EventHandler<SubtitleSegment>? SegmentAdded;

    public void AppendSegments(IEnumerable<SubtitleSegment> newSegments)
    {
        var added = newSegments.ToList();
        lock (_lock) { _segments.AddRange(added); }
        foreach (var seg in added)
            SegmentAdded?.Invoke(this, seg);
    }

    public async Task WriteSrtAsync(string outputPath)
    {
        List<SubtitleSegment> snapshot;
        lock (_lock) { snapshot = [.. _segments]; }

        var lines = snapshot.Select((s, i) =>
            $"{i + 1}\n{Fmt(s.StartTime)} --> {Fmt(s.EndTime)}\n{s.Text}\n");
        await File.WriteAllTextAsync(outputPath, string.Join("\n", lines));
    }

    public void Clear() { lock (_lock) _segments.Clear(); }

    private static string Fmt(TimeSpan t) =>
        $"{(int)t.TotalHours:D2}:{t.Minutes:D2}:{t.Seconds:D2},{t.Milliseconds:D3}";
}
