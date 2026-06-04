using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace WhisperLive.Infrastructure;

public sealed class AppSettings
{
    private static readonly string _path = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "whisper.live", "settings.json");

    private static readonly JsonSerializerOptions _json = new() { WriteIndented = true };

    public string ApiUrl { get; set; } = "http://localhost:9000";
    public string Language { get; set; } = "en";
    public string Model { get; set; } = "small";
    public int ChunkDurationSeconds { get; set; } = 5;
    public string Theme { get; set; } = "Default";

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
        catch { }
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
        catch { }
    }
}
