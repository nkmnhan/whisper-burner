using System;

namespace WhisperLive.Services.Audio;

/// <summary>
/// Thrown by <see cref="RecordingManager.StartAsync"/> when a recording session fails to start
/// (e.g. no enabled audio playback device, or the sessions folder cannot be created). The manager
/// rolls back to <see cref="Models.RecordingState.Idle"/> before throwing, so the UI can surface
/// <see cref="Exception.Message"/> and stay in a clean, restartable state.
/// </summary>
public sealed class RecordingStartException : Exception
{
    public RecordingStartException(string message, Exception inner) : base(message, inner) { }
}
