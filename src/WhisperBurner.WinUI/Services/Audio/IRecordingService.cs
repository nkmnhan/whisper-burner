using WhisperBurner.WinUI.Models;

namespace WhisperBurner.WinUI.Services.Audio;

public interface IRecordingService
{
    bool IsRecording { get; }

    event EventHandler<AudioChunkInfo>? AudioChunkReady;

    Task StartAsync(CaptureRegion? region, RecordingOptions options);
    Task StopAsync();
}
