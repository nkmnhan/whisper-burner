namespace WhisperBurner.WinUI.Infrastructure;

public sealed class AppLogger : IAppLogger
{
    private static readonly string _logPath = Path.Combine(AppSettings.DataRoot, "app.log");
    private static readonly object _lock = new();

    public static readonly AppLogger Instance = new();

    // Static convenience methods — existing call sites unchanged
    public static void Info(string message) => Write("INFO ", message);
    public static void Warn(string message) => Write("WARN ", message);
    public static void Error(string message, Exception? ex = null) =>
        Write("ERROR", ex is null ? message : $"{message}\n{ex}");
    public static void Clear() { try { File.Delete(_logPath); } catch { } }

    // IAppLogger explicit implementation
    void IAppLogger.Info(string message) => Info(message);
    void IAppLogger.Warn(string message) => Warn(message);
    void IAppLogger.Error(string message, Exception? ex) => Error(message, ex);

    private static void Write(string level, string message)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}";
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
}
