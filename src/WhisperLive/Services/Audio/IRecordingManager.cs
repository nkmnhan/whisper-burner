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
    event EventHandler? ApiStalled;
    // Fires when a recording session ends abnormally (e.g. the audio device disappeared). The
    // string is a user-facing message; State transitions to Idle alongside it.
    event EventHandler<string>? RecordingFaulted;
    // Fires ~20 times/second with normalised RMS amplitude (0.0–1.0) while recording.
    event EventHandler<float>? AudioLevelChanged;
    IReadOnlyList<string> GetRecentSegments();
    Task StartAsync(RecordingOptions options);
    Task StopAsync();
    Task PauseAsync();
    Task ResumeAsync();
}
