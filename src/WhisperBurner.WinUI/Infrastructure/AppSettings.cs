using System.Text.Json;

namespace WhisperBurner.WinUI.Infrastructure;

public sealed class AppSettings
{
    private static readonly string _path = Path.Combine(DataRoot, "settings.json");

    public static AppSettings Current { get; } = Load();

    public static string DataRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "whisper.burner");

    public static string SessionsRoot => Path.Combine(DataRoot, "sessions");
    public static string TempRoot => Path.Combine(DataRoot, "temp");

    public string ApiUrl { get; set; } = "http://localhost:5000";
    public string Model { get; set; } = "small";
    public string Language { get; set; } = "en";
    public int ChunkDurationSeconds { get; set; } = 3;
    public bool CaptureSystemAudio { get; set; } = true;

    public void Save()
    {
        Directory.CreateDirectory(DataRoot);
        File.WriteAllText(_path,
            JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static AppSettings Load()
    {
        try
        {
            if (File.Exists(_path))
            {
                var s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_path));
                if (s != null) return s;
            }
        }
        catch { }
        return new AppSettings();
    }
}
