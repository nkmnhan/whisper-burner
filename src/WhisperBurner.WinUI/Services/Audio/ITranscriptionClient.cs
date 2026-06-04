using WhisperBurner.WinUI.Models;

namespace WhisperBurner.WinUI.Services.Audio;

public interface ITranscriptionClient
{
    Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default);
    Task<ApiHealthInfo> GetHealthAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> GetAvailableModelsAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SubtitleSegment>> TranscribeChunkAsync(
        Stream audioChunk,
        string model,
        string language,
        CancellationToken cancellationToken = default);
}
