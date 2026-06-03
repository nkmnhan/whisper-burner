using WhisperBurner.WinUI.Models;

namespace WhisperBurner.WinUI.Services;

public interface ITranscriptionClient
{
    Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> GetAvailableModelsAsync(CancellationToken cancellationToken = default);

    // Transcribes a short audio chunk (WAV/PCM stream) and returns subtitle segments.
    Task<IReadOnlyList<SubtitleSegment>> TranscribeChunkAsync(
        Stream audioChunk,
        string model,
        string language,
        CancellationToken cancellationToken = default);
}
