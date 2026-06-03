namespace WhisperBurner.WinUI.Models;

public record SubtitleSegment(int Id, double Start, double End, string Text)
{
    public TimeSpan StartTime => TimeSpan.FromSeconds(Start);
    public TimeSpan EndTime => TimeSpan.FromSeconds(End);
}
