using System;
using System.IO;
using System.Threading.Tasks;

namespace WhisperLive.Services.Assistant;

/// <summary>
/// Saves assistant responses and conversations to
/// <c>~/whisper.live/sessions/</c>.
/// </summary>
public sealed class AssistantExportService : IAssistantExportService
{
    private static readonly string SessionDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "whisper.live", "sessions");

    public async Task<string> SaveAsync(string fileName, string text)
    {
        Directory.CreateDirectory(SessionDir);
        var path = Path.Combine(SessionDir, fileName);
        await File.WriteAllTextAsync(path, text);
        return path;
    }
}
