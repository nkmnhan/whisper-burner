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

    public string ApiUrl { get; set; } = "http://localhost:5000";
    public string Language { get; set; } = "en";
    public string Model { get; set; } = "small";
    public int ChunkDurationSeconds { get; set; } = 5;
    public string Theme { get; set; } = "Default";
    public List<string> ContextFolderPaths { get; set; } = [];
    public List<string> AllowedReadPaths { get; set; } = [];
    public string DefaultMeetingContext { get; set; } = "";
    public List<string> RecentMeetingContexts { get; set; } = [];
    public List<SavedPrompt> SavedMeetingContexts { get; set; } = [];

    // AI feature flag — off by default to prevent unexpected token usage
    // Assistant Q&A is always available — it only runs when the user sends a question.

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
