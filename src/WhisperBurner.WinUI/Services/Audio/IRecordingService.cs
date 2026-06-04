using WhisperBurner.WinUI.Models;

namespace WhisperBurner.WinUI.Services.Audio;

public interface IRecordingService
{
    bool IsRecording { get; }

    Func<AudioChunkInfo, Task>? AudioChunkReady { get; set; }

    Task StartAsync(CaptureRegion? region, RecordingOptions options);
    Task StopAsync();
}
