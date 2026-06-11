using System.Threading;
using System.Threading.Tasks;

namespace WhisperLive.Services.Assistant;

public interface ISessionAssistantService
{
    void StartSession(string? preContext = null);
    Task<string> AskAsync(string question, AskOptions? options = null, CancellationToken cancellationToken = default);
    void EndSession();
}
