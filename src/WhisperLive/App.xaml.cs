using Microsoft.UI.Xaml;
using WhisperLive.Helpers;
using WhisperLive.Infrastructure;
using WhisperLive.Overlay;
using WhisperLive.Services.Assistant;
using WhisperLive.Services.Audio;
using WhisperLive.Services.Translation;
using WhisperLive.Views;

namespace WhisperLive;

sealed partial class App : Application
{
    internal static MainWindow MainWindow { get; private set; } = null!;
    internal static CaptionOverlayWindow? CaptionOverlay { get; private set; }

    internal IRecordingService RecordingService { get; }
    internal ITranscriptionClient TranscriptionClient { get; }
    internal ISubtitleService SubtitleService { get; }
    internal IRecordingManager RecordingManager { get; }
    internal ISessionAssistantService SessionAssistant { get; }
    internal IAssistantExportService AssistantExport { get; }
    internal ITranslationService TranslationService { get; private set; }

    public App()
    {
        AppLogger.Initialize();
        InitializeComponent();

        var aiProvider = new ClaudeCliProvider();

        RecordingService = new Services.Audio.RecordingService();
        TranscriptionClient = new Services.Audio.TranscriptionClient();
        SubtitleService = new Services.Audio.SubtitleService();
        RecordingManager = new Services.Audio.RecordingManager(RecordingService, TranscriptionClient, SubtitleService);
        SessionAssistant = new SessionAssistantService(RecordingManager, aiProvider);
        AssistantExport = new AssistantExportService();

        // Disabled placeholder — replaced by ApplySettings() in LiveTranscriptPage.OnLoaded
        // once AppSettings are loaded asynchronously on the UI thread.
        TranslationService = BuildTranslationService(new AppSettings());

        UnhandledException += (_, e) =>
        {
            AppLogger.Error(e.Exception, "Unhandled exception: {Message}", e.Message);
            e.Handled = true;
        };
    }

    /// <summary>
    /// Rebuilds <see cref="TranslationService"/> from freshly-loaded settings.
    /// Called by <see cref="Views.LiveTranscriptPage"/> after its async settings load completes,
    /// before it subscribes to <see cref="ITranslationService.SegmentTranslated"/>.
    /// </summary>
    internal void ApplySettings(AppSettings settings)
    {
        TranslationService = BuildTranslationService(settings);
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        MainWindow = new MainWindow();
        WindowHelper.TrackWindow(MainWindow);
        MainWindow.Navigate(typeof(LiveTranscriptPage));
        MainWindow.Activate();

        CaptionOverlay = new CaptionOverlayWindow();
        CaptionOverlay.AppWindow.Hide();

        // Wire subtitle segments to the overlay here (not inside RecordingManager) — SRP.
        // CaptionOverlayWindow.ShowSegment already marshals to the UI thread internally.
        RecordingManager.SegmentAdded += (_, seg) => CaptionOverlay?.ShowSegment(seg);

        // Gallery pattern: close all tracked windows when main closes.
        MainWindow.Closed += async (s, _) =>
        {
            await RecordingManager.StopAsync();
            SessionAssistant.EndSession();
            TranslationService.EndSession();

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
        TitleBarHelper.ApplySystemThemeToCaptionButtons(MainWindow, ThemeHelper.ActualTheme);
    }

    internal static ITranslationService BuildTranslationService(AppSettings settings)
    {
        // Whisper built-in translate: task=translate sent with each audio chunk.
        // No external API needed — the translation happens inside the Docker container.
        if (settings.TranslationProvider == "whisper" && settings.EnableTranslation)
            return new Services.Translation.PassThroughTranslationService();

        ITranslationProvider provider = settings.TranslationProvider switch
        {
            "google" => new GoogleTranslationProvider(settings.GoogleTranslateApiKey),
            _ => new DeepLTranslationProvider(settings.DeepLApiKey),
        };
        return new Services.Translation.TranslationService(
            provider,
            isEnabled: settings.EnableTranslation,
            targetLanguage: settings.TranslationTargetLanguage);
    }
}
