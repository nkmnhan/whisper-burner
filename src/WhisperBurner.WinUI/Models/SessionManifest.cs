using System.Text.Json.Serialization;

namespace WhisperBurner.WinUI.Models;

public class SessionManifest
{
    public string Id { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public string VideoFile { get; set; } = "recording.mp4";
    public string SubtitleFile { get; set; } = "subtitles.srt";
    public string TranscriptFile { get; set; } = "transcript.json";
    public CaptureRegion? CaptureRegion { get; set; }
    public string Language { get; set; } = "en";
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public TranscriptionMode TranscriptionMode { get; set; }
    public string Model { get; set; } = "small";
}
