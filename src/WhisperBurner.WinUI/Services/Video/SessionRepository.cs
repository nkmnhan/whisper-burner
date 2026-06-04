using System.Text.Json;
using System.Text.Json.Serialization;
using WhisperBurner.WinUI.Infrastructure;
using WhisperBurner.WinUI.Models;

namespace WhisperBurner.WinUI.Services.Video;

public class SessionRepository : ISessionRepository
{
    private static readonly JsonSerializerOptions _json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public string GetSessionDirectory(string sessionId) =>
        Path.Combine(AppSettings.SessionsRoot, sessionId);

    public Task<SessionManifest> CreateSessionAsync(CaptureRegion? region, RecordingOptions options)
    {
        var id = $"{DateTime.Now:yyyy-MM-dd_HH-mm-ss}_{Guid.NewGuid().ToString("N")[..4]}";
        var dir = GetSessionDirectory(id);
        Directory.CreateDirectory(dir);

        var manifest = new SessionManifest
        {
            Id = id,
            CreatedAt = DateTimeOffset.Now,
            CaptureRegion = region,
            Language = options.Language,
            TranscriptionMode = TranscriptionMode.DockerApi,
            Model = options.Model,
        };
        return Task.FromResult(manifest);
    }

    public async Task SaveManifestAsync(SessionManifest manifest)
    {
        var path = Path.Combine(GetSessionDirectory(manifest.Id), "manifest.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(manifest, _json));
    }

    public async Task<SessionManifest?> GetSessionAsync(string sessionId)
    {
        var path = Path.Combine(GetSessionDirectory(sessionId), "manifest.json");
        if (!File.Exists(path)) return null;
        var json = await File.ReadAllTextAsync(path);
        return JsonSerializer.Deserialize<SessionManifest>(json, _json);
    }

    public async Task<IReadOnlyList<SessionManifest>> ListSessionsAsync()
    {
        if (!Directory.Exists(AppSettings.SessionsRoot))
            return [];

        var results = new List<SessionManifest>();
        foreach (var dir in Directory.GetDirectories(AppSettings.SessionsRoot).OrderDescending())
        {
            var m = await GetSessionAsync(Path.GetFileName(dir));
            if (m != null) results.Add(m);
        }
        return results;
    }
}
