using WhisperBurner.WinUI.Models;

namespace WhisperBurner.WinUI.Services;

public interface ISubtitleService
{
    IReadOnlyList<SubtitleSegment> Segments { get; }

    // Raised after each batch of new segments is merged in.
    event EventHandler<SubtitleSegment>? SegmentAdded;

    void StartSession(string ndjsonPath);
    void EndSession();
    void AppendSegments(IEnumerable<SubtitleSegment> segments);
    Task WriteSrtAsync(string outputPath);
    void Clear();
}
