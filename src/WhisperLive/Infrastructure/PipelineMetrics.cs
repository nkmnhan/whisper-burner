using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace WhisperLive.Infrastructure;

/// <summary>
/// Thread-safe pipeline benchmark collector. Measures transcription and translation latency
/// per session and logs a summary with avg / min / max / p95 on session end.
/// </summary>
public sealed class PipelineMetrics
{
    public static readonly PipelineMetrics Instance = new();
    private PipelineMetrics() { }

    private readonly StageMetrics _transcription = new("Transcription");
    private readonly StageMetrics _translation    = new("Translation");

    private long _chunksTotal;
    private long _chunksBytesTotal;
    private long _segmentsProduced;
    private long _segmentsTranslated;

    public void RecordTranscription(double elapsedMs, long bytes, int segmentCount)
    {
        _transcription.Record(elapsedMs);
        Interlocked.Add(ref _chunksBytesTotal, bytes);
        Interlocked.Increment(ref _chunksTotal);
        Interlocked.Add(ref _segmentsProduced, segmentCount);
        AppLogger.Debug("[Metrics] Transcription: {Ms:F0}ms  {KB:F1}KB -> {Segs} seg(s)",
            elapsedMs, bytes / 1024.0, segmentCount);
    }

    public void RecordTranslation(double elapsedMs)
    {
        _translation.Record(elapsedMs);
        Interlocked.Increment(ref _segmentsTranslated);
        AppLogger.Debug("[Metrics] Translation: {Ms:F0}ms", elapsedMs);
    }

    public void Reset()
    {
        _transcription.Reset();
        _translation.Reset();
        Interlocked.Exchange(ref _chunksTotal, 0);
        Interlocked.Exchange(ref _chunksBytesTotal, 0);
        Interlocked.Exchange(ref _segmentsProduced, 0);
        Interlocked.Exchange(ref _segmentsTranslated, 0);
    }

    public void LogSummary()
    {
        var chunks     = Interlocked.Read(ref _chunksTotal);
        var bytes      = Interlocked.Read(ref _chunksBytesTotal);
        var segs       = Interlocked.Read(ref _segmentsProduced);
        var translated = Interlocked.Read(ref _segmentsTranslated);

        AppLogger.Info("[Metrics] ========== Pipeline Session Summary ==========");
        AppLogger.Info("[Metrics] Chunks: {Chunks}  Audio: {KB:F1} KB  Segments: {Segs}  Translated: {Trans}",
            chunks, bytes / 1024.0, segs, translated);
        _transcription.Log();
        _translation.Log();
        AppLogger.Info("[Metrics] =================================================");
    }

    /// <summary>Returns a point-in-time snapshot of the current session's metrics.</summary>
    public PipelineSnapshot GetSnapshot()
    {
        var samples = _transcription.GetSamples();

        double avg = 0, p95 = 0, min = 0, max = 0;
        if (samples.Length > 0)
        {
            Array.Sort(samples);
            avg = samples.Average();
            p95 = samples[(int)Math.Min(samples.Length - 1, Math.Ceiling(samples.Length * 0.95) - 1)];
            min = samples[0];
            max = samples[^1];
        }

        return new PipelineSnapshot(
            ChunksProcessed:  Interlocked.Read(ref _chunksTotal),
            SegmentsProduced: Interlocked.Read(ref _segmentsProduced),
            AvgLatencyMs:     Math.Round(avg, 0),
            P95LatencyMs:     Math.Round(p95, 0),
            MinLatencyMs:     Math.Round(min, 0),
            MaxLatencyMs:     Math.Round(max, 0));
    }
}

public record PipelineSnapshot(
    long ChunksProcessed,
    long SegmentsProduced,
    double AvgLatencyMs,
    double P95LatencyMs,
    double MinLatencyMs,
    double MaxLatencyMs);

internal sealed class StageMetrics(string name)
{
    private readonly List<double> _samples = [];
    private readonly Lock _lock = new();

    public void Record(double ms)    { lock (_lock) _samples.Add(ms); }
    public void Reset()              { lock (_lock) _samples.Clear(); }
    public double[] GetSamples()     { lock (_lock) return [.. _samples]; }

    public void Log()
    {
        double[] arr;
        lock (_lock) arr = [.. _samples];

        if (arr.Length == 0)
        {
            AppLogger.Info("[Metrics] {Stage}: no samples", name);
            return;
        }

        Array.Sort(arr);
        var avg = arr.Average();
        var p95 = arr[(int)Math.Min(arr.Length - 1, Math.Ceiling(arr.Length * 0.95) - 1)];

        AppLogger.Info("[Metrics] {Stage}: n={N}  avg={Avg:F0}ms  min={Min:F0}ms  max={Max:F0}ms  p95={P95:F0}ms",
            name, arr.Length, avg, arr[0], arr[^1], p95);
    }
}
