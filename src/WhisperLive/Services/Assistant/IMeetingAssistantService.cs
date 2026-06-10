using System.Threading;
using System.Threading.Tasks;

namespace WhisperLive.Services.Assistant;

public interface IMeetingAssistantService
{
    void StartSession(string? preContext = null);
    Task<string> AskAsync(string question, bool includeFullTranscript = false, CancellationToken cancellationToken = default);
    void EndSession();
}
