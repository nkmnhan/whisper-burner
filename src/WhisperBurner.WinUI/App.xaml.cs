using Microsoft.UI.Xaml;
using WhisperBurner.WinUI.Services.Audio;
using WhisperBurner.WinUI.Services.Video;

namespace WhisperBurner.WinUI;

public partial class App : Application
{
    public static MainWindow? MainWindow { get; private set; }

    public IRecordingService    RecordingService    { get; } = new RecordingService();
    public ITranscriptionClient TranscriptionClient { get; } = new TranscriptionClient();
    public ISubtitleService     SubtitleService     { get; } = new SubtitleService();
    public ISessionRepository   SessionRepository   { get; } = new SessionRepository();

    public App()
    {
        Program.Trace("App() ctor: before InitializeComponent");
        try
        {
            InitializeComponent();
            Program.Trace("App() ctor: InitializeComponent done");
        }
        catch (Exception ex)
        {
            Program.Trace($"App() ctor: InitializeComponent THREW:\n{ex}");
            throw;
        }

        UnhandledException += (_, e) =>
        {
            var msg = $"UnhandledException: {e.Message}\n{e.Exception}";
            Program.Trace($"UnhandledException: {e.Message}");
            WriteCrashLog(msg);
        };
        Program.Trace("App() ctor: UnhandledException registered");
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        Program.Trace("OnLaunched entered");
        try
        {
            Program.Trace("OnLaunched: new MainWindow()...");
            MainWindow = new MainWindow();
            Program.Trace("OnLaunched: MainWindow.Activate()...");
            MainWindow.Activate();
            Program.Trace("OnLaunched: NavigateToRecording()...");
            MainWindow.NavigateToRecording();
            Program.Trace("OnLaunched: completed OK");
        }
        catch (Exception ex)
        {
            Program.Trace($"OnLaunched THREW:\n{ex}");
            WriteCrashLog($"OnLaunched threw: {ex}");
        }
    }

    private static void WriteCrashLog(string content)
    {
        try
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "whisper.burner");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "crash.log"), content);
        }
        catch { }
    }
}
