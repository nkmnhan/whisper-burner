using WhisperBurner.WinUI.Models;

namespace WhisperBurner.WinUI.Services;

public interface ISessionRepository
{
    string GetSessionDirectory(string sessionId);
    Task<SessionManifest> CreateSessionAsync(CaptureRegion region, RecordingOptions options);
    Task SaveManifestAsync(SessionManifest manifest);
    Task<SessionManifest?> GetSessionAsync(string sessionId);
    Task<IReadOnlyList<SessionManifest>> ListSessionsAsync();
}
