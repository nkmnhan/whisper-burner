using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using WhisperLive.Models;

namespace WhisperLive.Services.Audio;

public interface ISubtitleService
{
    event EventHandler<SubtitleSegment> SegmentAdded;
    IReadOnlyList<SubtitleSegment> AllSegments { get; }
    string? CurrentSessionPath { get; }
    string SessionsDirectory { get; }
    void StartSession();
    void EndSession();
    void AppendSegments(IEnumerable<SubtitleSegment> segments);
    void ApplyCorrections(IReadOnlyList<CorrectedSegment> corrections);
    Task ExportSrtAsync(string path);
}
