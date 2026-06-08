using Microsoft.UI.Xaml;
using WhisperLive.Helpers;
using WhisperLive.Infrastructure;
using WhisperLive.Overlay;
using WhisperLive.Services;
using WhisperLive.Views;

namespace WhisperLive;

sealed partial class App : Application
{
    internal static MainWindow MainWindow { get; private set; } = null!;
    internal static CaptionOverlayWindow? CaptionOverlay { get; private set; }

    internal RecordingService RecordingService { get; } = new();
    internal TranscriptionClient TranscriptionClient { get; } = new();
    internal SubtitleService SubtitleService { get; } = new();
    internal RecordingManager RecordingManager { get; }
    internal MeetingAssistantService MeetingAssistant { get; }

    public App()
    {
        AppLogger.Initialize();
        InitializeComponent();
        RecordingManager = new RecordingManager(RecordingService, TranscriptionClient, SubtitleService);
        MeetingAssistant = new MeetingAssistantService(RecordingManager);
        UnhandledException += (_, e) =>
        {
            AppLogger.Error(e.Exception, "Unhandled exception: {Message}", e.Message);
            e.Handled = true;
        };
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        MainWindow = new MainWindow();
        WindowHelper.TrackWindow(MainWindow);
        MainWindow.Navigate(typeof(LiveTranscriptPage));
        MainWindow.Activate();

        CaptionOverlay = new CaptionOverlayWindow();
        CaptionOverlay.AppWindow.Hide();

        // Gallery pattern: close all tracked windows when main closes.
        // Also clean up recording state and flush logs.
        MainWindow.Closed += async (s, _) =>
        {
            await RecordingManager.StopAsync();

            CaptionOverlay?.Close();

            var activeWindows = new System.Collections.Generic.List<Window>(WindowHelper.ActiveWindows);
            foreach (var w in activeWindows)
            {
                if (!w.Equals(s))
                    try { w.Close(); } catch { }
            }

            AppLogger.CloseAndFlush();
        };

        _ = InitializeThemeAsync();
        AppLogger.Info("Main window launched");
    }

    private static async System.Threading.Tasks.Task InitializeThemeAsync()
    {
        await ThemeHelper.InitializeAsync();
        // Update caption button colours to match restored theme
        TitleBarHelper.ApplySystemThemeToCaptionButtons(MainWindow, ThemeHelper.ActualTheme);
    }
}
