using Microsoft.UI.Xaml;
using WhisperLive.Helpers;
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

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        MainWindow = new MainWindow();
        WindowHelper.TrackWindow(MainWindow);
        ThemeHelper.Initialize();
        MainWindow.Navigate(typeof(LiveTranscriptPage));
        MainWindow.Activate();

        CaptionOverlay = new CaptionOverlayWindow();
        // Overlay shown/hidden by the recording page; start hidden
        CaptionOverlay.AppWindow.Hide();
    }
}
