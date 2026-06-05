namespace WhisperLive.Models;

/// <param name="OffsetSeconds">Actual wall-clock start time of this WAV file (includes overlap prefix).</param>
/// <param name="OverlapSeconds">Duration of overlap prefix prepended from the previous chunk. Segments with
/// WAV-relative Start &lt; OverlapSeconds belong to the previous chunk and must be skipped.</param>
public record AudioChunkInfo(string FilePath, int ChunkIndex, double OffsetSeconds, double OverlapSeconds = 0.0);
