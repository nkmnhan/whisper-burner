using System.Threading.Tasks;

namespace WhisperLive.Services.Assistant;

/// <summary>Persists assistant responses and conversations to disk.</summary>
public interface IAssistantExportService
{
    /// <summary>
    /// Saves <paramref name="text"/> to the sessions folder using the given
    /// <paramref name="fileName"/>. Returns the full path written.
    /// </summary>
    Task<string> SaveAsync(string fileName, string text);
}
