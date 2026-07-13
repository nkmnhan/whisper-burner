namespace WhisperLive.Models;

/// <param name="WavData">Complete in-memory WAV file (RIFF header + PCM), ready to POST to the API.</param>
/// <param name="OffsetSeconds">Actual wall-clock start time of this chunk (includes overlap prefix).</param>
/// <param name="OverlapSeconds">Duration of overlap prefix prepended from the previous chunk. Segments with
/// WAV-relative Start &lt; OverlapSeconds belong to the previous chunk and must be skipped.</param>
public record AudioChunkInfo(byte[] WavData, int ChunkIndex, double OffsetSeconds, double OverlapSeconds = 0.0);
