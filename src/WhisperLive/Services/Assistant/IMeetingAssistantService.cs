using System;
using System.Threading;
using System.Threading.Tasks;
using WhisperLive.Models;

namespace WhisperLive.Services.Assistant;

public interface IMeetingAssistantService
{
    event EventHandler<MeetingNotes>? NotesUpdated;
    event EventHandler? NotesRefreshStarted;
    void StartSession(string? preContext = null);
    /// <param name="threadIndex">0 = Thread 1 (default), 1 = Thread 2 (lazy — created on first use).</param>
    Task<string> AskAsync(string question, int threadIndex = 0, CancellationToken cancellationToken = default);
    Task RefreshNotesAsync(CancellationToken cancellationToken = default, bool force = false);
    void EndSession();
}
