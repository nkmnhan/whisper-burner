using System;

namespace WhisperLive.Models;

public record SubtitleSegment(int Id, double Start, double End, string Text)
{
    public string ToSrtEntry()
    {
        return $"{Id}\n{FormatTime(Start)} --> {FormatTime(End)}\n{Text}\n";
    }

    private static string FormatTime(double seconds)
    {
        var ts = TimeSpan.FromSeconds(seconds);
        return $"{ts.Hours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2},{ts.Milliseconds:D3}";
    }
}
