using WhisperBurner.WinUI.Models;

namespace WhisperBurner.WinUI.Services;

public interface IRecordingService
{
    bool IsRecording { get; }

    // Raised each time an audio chunk is ready for transcription.
    // Payload is a path to a temp WAV/PCM file.
    event EventHandler<string>? AudioChunkReady;

    Task StartAsync(CaptureRegion region, RecordingOptions options, string outputMp4Path);
    Task StopAsync();
}
