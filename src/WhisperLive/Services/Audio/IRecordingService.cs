using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using WhisperLive.Models;

namespace WhisperLive.Services.Audio;

public interface IRecordingService : IDisposable
{
    ChannelReader<AudioChunkInfo> Chunks { get; }
    bool IsPaused { get; }
    // Fires ~20 times/second with normalised RMS amplitude (0.0–1.0) from the loopback buffer.
    event EventHandler<float>? AudioLevelChanged;
    // Fires when capture ends unexpectedly (device removed/changed), not on a normal StopAsync.
    event EventHandler<Exception?>? CaptureStopped;
    Task StartAsync(RecordingOptions options, CancellationToken ct);
    Task StopAsync();
    Task PauseAsync();
    Task ResumeAsync();
}
