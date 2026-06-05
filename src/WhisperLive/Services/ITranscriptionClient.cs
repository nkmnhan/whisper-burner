using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using WhisperLive.Models;

namespace WhisperLive.Services;

public interface ITranscriptionClient
{
    Task<bool> CheckHealthAsync(string apiUrl, CancellationToken ct = default);

    Task<IEnumerable<SubtitleSegment>> TranscribeChunkAsync(
        AudioChunkInfo chunk, RecordingOptions options, CancellationToken ct);
}
