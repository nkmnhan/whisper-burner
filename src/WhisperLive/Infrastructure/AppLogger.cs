using Serilog;
using Serilog.Events;
using System;
using System.IO;

namespace WhisperLive.Infrastructure;

/// <summary>
/// Static logger wrapper around Serilog.
/// Call <see cref="Initialize"/> once at startup; use the typed methods everywhere else.
/// Logs go to: ~/whisper.live/logs/app-.log (daily rolling, 7-day retention)
/// and to the VS Debug output window in DEBUG builds.
/// </summary>
public static class AppLogger
{
    private static readonly string _logDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "whisper.live", "logs");

    public static void Initialize()
    {
        Directory.CreateDirectory(_logDir);

        var config = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .Enrich.WithProperty("App", "WhisperLive")
            .WriteTo.File(
                path: Path.Combine(_logDir, "app-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7,
                flushToDiskInterval: TimeSpan.FromMilliseconds(200),
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}");

#if DEBUG
        config.WriteTo.Debug(
            outputTemplate: "[{Level:u3}] {Message:lj}{NewLine}{Exception}");
#endif

        Log.Logger = config.CreateLogger();
        Log.Information("WhisperLive started");
    }

    public static void CloseAndFlush() => Log.CloseAndFlush();

    public static void Debug(string message, params object[] args) =>
        Log.Debug(message, args);

    public static void Info(string message, params object[] args) =>
        Log.Information(message, args);

    public static void Warning(string message, params object[] args) =>
        Log.Warning(message, args);

    public static void Warning(Exception ex, string message, params object[] args) =>
        Log.Warning(ex, message, args);

    public static void Error(string message, params object[] args) =>
        Log.Error(message, args);

    public static void Error(Exception ex, string message, params object[] args) =>
        Log.Error(ex, message, args);
}
