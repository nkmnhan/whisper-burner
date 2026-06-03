using WhisperBurner.WinUI.Models;

namespace WhisperBurner.WinUI.Services;

public interface IRecordingService
{
    bool IsRecording { get; }

    // Raised each time an audio chunk is ready for transcription.
    // Payload carries the temp WAV path and the session-relative start offset in seconds.
    event EventHandler<AudioChunkInfo>? AudioChunkReady;

    Task StartAsync(CaptureRegion region, RecordingOptions options);
    Task StopAsync();
}
