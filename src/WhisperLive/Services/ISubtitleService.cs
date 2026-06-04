using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using WhisperLive.Models;

namespace WhisperLive.Services;

public interface ISubtitleService
{
    event EventHandler<SubtitleSegment> SegmentAdded;
    IReadOnlyList<SubtitleSegment> AllSegments { get; }
    void AppendSegments(IEnumerable<SubtitleSegment> segments);
    Task ExportSrtAsync(string path);
}
