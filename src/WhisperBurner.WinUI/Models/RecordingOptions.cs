namespace WhisperBurner.WinUI.Models;

public record RecordingOptions
{
    public string Model { get; init; } = "small";
    public string Language { get; init; } = "en";
    public TranscriptionMode TranscriptionMode { get; init; } = TranscriptionMode.DockerApi;
    public int ChunkDurationSeconds { get; init; } = 3;
    public bool CaptureSystemAudio { get; init; } = false;
    public bool CaptureMicrophone { get; init; } = true;
}

public enum TranscriptionMode
{
    DockerApi,
    LocalModel
}
