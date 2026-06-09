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
    Task<string> AskAsync(string question, CancellationToken cancellationToken = default);
    Task RefreshNotesAsync(CancellationToken cancellationToken = default, bool force = false);
    void EndSession();
}
