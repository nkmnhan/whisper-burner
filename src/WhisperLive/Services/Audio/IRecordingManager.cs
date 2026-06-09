using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using WhisperLive.Models;

namespace WhisperLive.Services.Audio;

public interface IRecordingManager
{
    RecordingState State { get; }
    string? CurrentSessionPath { get; }
    event EventHandler<RecordingState>? StateChanged;
    event EventHandler<SubtitleSegment>? SegmentAdded;
    IReadOnlyList<string> GetRecentSegments();
    Task StartAsync(RecordingOptions options);
    Task StopAsync();
    Task PauseAsync();
    Task ResumeAsync();
}
