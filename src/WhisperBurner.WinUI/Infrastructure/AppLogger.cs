using WhisperBurner.WinUI.Infrastructure;

namespace WhisperBurner.WinUI.Infrastructure;

public static class AppLogger
{
    private static readonly string _logPath = Path.Combine(AppSettings.DataRoot, "app.log");
    private static readonly object _lock = new();

    public static void Log(string category, string message)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{category}] {message}";
        try
        {
            lock (_lock)
            {
                Directory.CreateDirectory(AppSettings.DataRoot);
                File.AppendAllText(_logPath, line + Environment.NewLine);
            }
        }
        catch { }
    }

    public static void Info(string message) => Log("INFO", message);
    public static void Error(string message, Exception? ex = null) =>
        Log("ERROR", ex == null ? message : $"{message} — {ex.GetType().Name}: {ex.Message}");
    public static void Warn(string message) => Log("WARN", message);

    public static void Clear()
    {
        try { File.Delete(_logPath); } catch { }
    }
}
