using System;
using System.Threading;
using System.Threading.Tasks;
using WhisperLive.Models;

namespace WhisperLive.Services;

public interface IMeetingAssistantService
{
    event EventHandler<MeetingNotes>? NotesUpdated;
    void StartSession();
    Task<string> AskAsync(string question, CancellationToken cancellationToken = default);
    Task RefreshNotesAsync(CancellationToken cancellationToken = default);
    void EndSession();
}
