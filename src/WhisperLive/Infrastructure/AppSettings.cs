using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using WhisperLive.Models;

namespace WhisperLive.Infrastructure;

public sealed class AppSettings
{
    private static readonly string _path = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "whisper.live", "settings.json");

    private static readonly JsonSerializerOptions _json = new() { WriteIndented = true };

    public string ApiUrl { get; set; } = "http://127.0.0.1:5000";
    public string Language { get; set; } = "auto";
    public string Model { get; set; } = "small";
    public int ChunkDurationSeconds { get; set; } = 5;
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
    public string DeepLApiKey { get; set; } = "";
    public string GoogleTranslateApiKey { get; set; } = "";

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

    public async Task SaveAsync()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var json = JsonSerializer.Serialize(this, _json);
            await File.WriteAllTextAsync(_path, json);
        }
            catch (Exception ex) { AppLogger.Warning(ex, "Failed to save settings"); }
    }
}
