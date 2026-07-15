using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WhisperLive.Models;

namespace WhisperLive.Infrastructure;

public sealed class AppSettings
{
    private static readonly string _path = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "whisper.live", "settings.json");

    private static readonly JsonSerializerOptions _json = new() { WriteIndented = true };

    // Serialises all writes to settings.json. Multiple callers (Settings page, Live page,
    // CLI pipe thread) save concurrently; without this a File.Write* would hit a sharing
    // violation and silently drop that update. Combined with the atomic temp-file replace in
    // SaveAsync, a partially-written file is never observed.
    private static readonly SemaphoreSlim _saveGate = new(1, 1);

    public string ApiUrl { get; set; } = "http://127.0.0.1:5000";
    public string Language { get; set; } = "auto";
    public string Model { get; set; } = "small";
    public int ChunkDurationSeconds { get; set; } = 7;
    public string Theme { get; set; } = "Default";
    public List<string> ContextFolderPaths { get; set; } = [];
    public List<string> AllowedReadPaths { get; set; } = [];
    public string DefaultSessionContext { get; set; } = "";
    public List<string> RecentSessionContexts { get; set; } = [];
    public List<SavedPrompt> SavedSessionContexts { get; set; } = [];
    public bool EnableAssistant { get; set; } = true;
    public List<SessionSkill> CustomSkills { get; set; } = [];

    public bool EnableTranslation { get; set; } = false;
    public string TranslationTargetLanguage { get; set; } = "vi";
    public string TranslationProvider { get; set; } = "docker";

    // Stored DPAPI-protected on disk (see SecretProtector). Never bind UI or pass to providers
    // directly — use the Get*/Set* accessors below so plaintext exists only transiently in memory.
    public string DeepLApiKey { get; set; } = "";
    public string GoogleTranslateApiKey { get; set; } = "";

    public string GetDeepLApiKey() => SecretProtector.Unprotect(DeepLApiKey);
    public void SetDeepLApiKey(string plaintext) => DeepLApiKey = SecretProtector.Protect(plaintext);

    public string GetGoogleApiKey() => SecretProtector.Unprotect(GoogleTranslateApiKey);
    public void SetGoogleApiKey(string plaintext) => GoogleTranslateApiKey = SecretProtector.Protect(plaintext);

    /// <summary>Validates an API base URL: well-formed absolute http/https URI.</summary>
    public static bool IsValidApiUrl(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
        (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    public static async Task<AppSettings> LoadAsync()
    {
        try
        {
            if (File.Exists(_path))
            {
                var json = await File.ReadAllTextAsync(_path);
                return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            }
        }
        catch (Exception ex) { AppLogger.Warning(ex, "Failed to load settings, using defaults"); }
        return new AppSettings();
    }

    /// <summary>Shallow copy with fresh list instances so a mutated copy never races readers of the original.</summary>
    public AppSettings Clone() => new()
    {
        ApiUrl = ApiUrl,
        Language = Language,
        Model = Model,
        ChunkDurationSeconds = ChunkDurationSeconds,
        Theme = Theme,
        ContextFolderPaths = [.. ContextFolderPaths],
        AllowedReadPaths = [.. AllowedReadPaths],
        DefaultSessionContext = DefaultSessionContext,
        RecentSessionContexts = [.. RecentSessionContexts],
        SavedSessionContexts = [.. SavedSessionContexts],
        EnableAssistant = EnableAssistant,
        CustomSkills = [.. CustomSkills],
        EnableTranslation = EnableTranslation,
        TranslationTargetLanguage = TranslationTargetLanguage,
        TranslationProvider = TranslationProvider,
        DeepLApiKey = DeepLApiKey,
        GoogleTranslateApiKey = GoogleTranslateApiKey,
    };

    public async Task SaveAsync()
    {
        // Normalise secrets to their protected form so nothing is ever written in cleartext,
        // including legacy values loaded from an older settings.json.
        DeepLApiKey = SecretProtector.Protect(SecretProtector.Unprotect(DeepLApiKey));
        GoogleTranslateApiKey = SecretProtector.Protect(SecretProtector.Unprotect(GoogleTranslateApiKey));

        var json = JsonSerializer.Serialize(this, _json);
        await _saveGate.WaitAsync();
        try
        {
            var dir = Path.GetDirectoryName(_path)!;
            Directory.CreateDirectory(dir);
            // Atomic replace: write to a temp file then move over the target so a concurrent
            // reader never sees a half-written file and a failed write can't truncate the old one.
            var tmp = Path.Combine(dir, $"settings.{Guid.NewGuid():N}.tmp");
            await File.WriteAllTextAsync(tmp, json);
            File.Move(tmp, _path, overwrite: true);
        }
        catch (Exception ex) { AppLogger.Warning(ex, "Failed to save settings"); }
        finally { _saveGate.Release(); }
    }
}
