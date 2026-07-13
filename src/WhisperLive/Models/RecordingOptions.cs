namespace WhisperLive.Models;

public record RecordingOptions(
    string Language,
    int ChunkDurationSeconds,
    string ApiUrl,
    string Model,
    string? InitialPrompt = null,
    string Task = "transcribe");
