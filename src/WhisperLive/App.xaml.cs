using Microsoft.UI.Xaml;
using WhisperLive.Helpers;
using WhisperLive.Infrastructure;
using WhisperLive.Components;
using WhisperLive.Models;
using WhisperLive.Services.Assistant;
using WhisperLive.Services.Audio;
using WhisperLive.Services.Translation;
using WhisperLive.Services.Translation.Providers;
using WhisperLive.ViewModels;
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
    internal TranscriptViewModel TranscriptViewModel { get; private set; } = null!;

    private AppSettings _currentSettings = new();

    public App()
    {
        AppLogger.Initialize();
        InitializeComponent();

        var aiProvider = new ClaudeCliProvider();

        RecordingService = new Services.Audio.RecordingService();
        TranscriptionClient = new Services.Audio.TranscriptionClient();
        SubtitleService = new Services.Audio.SubtitleService();
        RecordingManager = new Services.Audio.RecordingManager(RecordingService, TranscriptionClient, SubtitleService);
        SessionAssistant = new SessionAssistantService(RecordingManager, aiProvider, () => _currentSettings);
        AssistantExport = new AssistantExportService();

        TranslationService = BuildTranslationService(new AppSettings());

        UnhandledException += (_, e) =>
        {
            AppLogger.Error(e.Exception, "Unhandled exception: {Message}", e.Message);
            e.Handled = true;
        };

        _ = ClaudeCliProvider.EnsureGlobalClaudeMdAsync();
    }

    internal void ApplySettings(AppSettings settings)
    {
        _currentSettings = settings;

        var isRecording = RecordingManager.State != RecordingState.Idle;

        TranslationService.EndSession();
        TranslationService.SegmentTranslated -= OnTranscriptSegmentTranslated;
        TranslationService = BuildTranslationService(settings);
        TranslationService.SegmentTranslated += OnTranscriptSegmentTranslated;
        if (isRecording)
            TranslationService.StartSession();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        MainWindow = new MainWindow();
        WindowHelper.TrackWindow(MainWindow);

        TranscriptViewModel = new TranscriptViewModel(
            MainWindow.DispatcherQueue,
            () => TranslationService.IsEnabled);

        MainWindow.Navigate(typeof(LiveTranscriptPage));
        MainWindow.Activate();

        CaptionOverlay = new CaptionOverlayWindow();
        CaptionOverlay.AppWindow.Hide();

        RecordingManager.SegmentAdded += (_, seg) => TranscriptViewModel.OnSegmentAdded(seg);
        RecordingManager.SegmentAdded += (_, seg) => TranslationService.EnqueueSegment(seg);
        TranslationService.SegmentTranslated += OnTranscriptSegmentTranslated;

        MainWindow.Closed += async (s, _) =>
        {
            await RecordingManager.StopAsync();
            TranscriptViewModel.FinalizeSession();
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

    private void OnTranscriptSegmentTranslated(object? sender, SegmentTranslationReadyEventArgs e) =>
        TranscriptViewModel.OnSegmentTranslated(e.SegmentId, e.TranslatedText);

    private static async System.Threading.Tasks.Task InitializeThemeAsync()
    {
        await ThemeHelper.InitializeAsync();
        TitleBarHelper.ApplySystemThemeToCaptionButtons(MainWindow, ThemeHelper.ActualTheme);
    }

    internal static ITranslationService BuildTranslationService(AppSettings settings)
    {
        if (!settings.EnableTranslation)
            return new DisabledTranslationService();

        ITranslationProvider provider = settings.TranslationProvider switch
        {
            "google" => new GoogleTranslationProvider(settings.GoogleTranslateApiKey),
            "whisper" => new DockerTranslationProvider(settings.ApiUrl),
            _        => new DeepLTranslationProvider(settings.DeepLApiKey),
        };

        return new TranslationService(
            provider,
            targetLanguage: settings.TranslationTargetLanguage);
    }
}
