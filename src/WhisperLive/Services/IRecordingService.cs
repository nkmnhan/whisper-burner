using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using WhisperLive.Models;

namespace WhisperLive.Services;

public interface IRecordingService : IDisposable
{
    ChannelReader<AudioChunkInfo> Chunks { get; }
    bool IsPaused { get; }
    Task StartAsync(RecordingOptions options, CancellationToken ct);
    Task StopAsync();
    Task PauseAsync();
    Task ResumeAsync();
}
