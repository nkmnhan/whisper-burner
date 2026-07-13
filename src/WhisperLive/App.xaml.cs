using Microsoft.UI.Xaml;
using System;
using System.Net.Http;
using System.Threading;
using Microsoft.Extensions.DependencyInjection;
using WhisperLive.Helpers;
using WhisperLive.Infrastructure;
using WhisperLive.Components;
using WhisperLive.Models;
using WhisperLive.Services.Assistant;
using WhisperLive.Services.Audio;
using WhisperLive.Services.Cli;
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
    // Volatile: ApplySettings() swaps this on the UI/CLI thread while the
    // RecordingManager.SegmentAdded handler reads it on a background thread.
    private volatile ITranslationService _translationService = null!;
    internal ITranslationService TranslationService
    {
        get => _translationService;
        private set => _translationService = value;
    }
    internal TranscriptViewModel TranscriptViewModel { get; private set; } = null!;

    private volatile AppSettings _currentSettings = new();

    // IHttpClientFactory manages handler pooling and lifetime — see RegisterHttpClients().
    // The ServiceProvider is held here to prevent the factory from being GC'd.
    private readonly IServiceProvider _serviceProvider;
    private readonly System.Net.Http.IHttpClientFactory _httpClientFactory;
    private CliPipeServer? _cliServer;

    public App()
    {
        AppLogger.Initialize();
        InitializeComponent();

        _serviceProvider = RegisterHttpClients();
        _httpClientFactory = _serviceProvider.GetRequiredService<System.Net.Http.IHttpClientFactory>();

        var aiProvider = new ClaudeCliProvider();

        RecordingService = new Services.Audio.RecordingService();
        TranscriptionClient = new Services.Audio.TranscriptionClient(_httpClientFactory);
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

    /// <summary>
    /// Registers named HttpClients with IHttpClientFactory.
    /// The transcription client uses a SocketsHttpHandler with PooledConnectionLifetime to
    /// evict connections before the Docker/uvicorn server's keep-alive timeout closes them,
    /// preventing SocketException (995/10054) on stale reused sockets.
    /// </summary>
    private static IServiceProvider RegisterHttpClients()
    {
        var services = new ServiceCollection();

        services.AddHttpClient(Services.Audio.TranscriptionClient.ClientName, client =>
        {
            client.Timeout = Timeout.InfiniteTimeSpan;
        })
        .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
            PooledConnectionIdleTimeout = TimeSpan.FromSeconds(30),
            MaxConnectionsPerServer = 4,
        });

        services.AddHttpClient(DockerTranslationProvider.ClientName, client =>
            client.Timeout = TimeSpan.FromSeconds(10));

        services.AddHttpClient(DeepLTranslationProvider.ClientName, client =>
            client.Timeout = TimeSpan.FromSeconds(10));

        services.AddHttpClient(GoogleTranslationProvider.ClientName, client =>
            client.Timeout = TimeSpan.FromSeconds(10));

        return services.BuildServiceProvider();
    }

    internal void ApplySettings(AppSettings settings)
    {
        _currentSettings = settings;

        var isRecording = RecordingManager.State != RecordingState.Idle;

        var oldService = TranslationService;
        oldService.EndSession();
        oldService.SegmentTranslated -= OnTranscriptSegmentTranslated;

        TranslationService = BuildTranslationService(settings);
        TranslationService.SegmentTranslated += OnTranscriptSegmentTranslated;
        if (isRecording)
            TranslationService.StartSession();

        oldService.Dispose();
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

        // Start/stop TranslationService in sync with RecordingManager state changes.
        // This ensures translation starts regardless of whether recording is triggered
        // from the UI or via the CLI pipe server.
        RecordingManager.StateChanged += (_, state) =>
        {
            if (state == RecordingState.Recording)
                TranslationService.StartSession();
            else if (state == RecordingState.Idle)
                TranslationService.EndSession();
        };

        MainWindow.Closed += async (s, _) =>
        {
            try
            {
                await RecordingManager.StopAsync().WaitAsync(TimeSpan.FromSeconds(5));
            }
            catch (TimeoutException)
            {
                AppLogger.Warning("StopAsync timed out on window close — forcing shutdown");
            }
            catch (OperationCanceledException) { }

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
            _cliServer?.Dispose();
        };

        _ = InitializeThemeAsync();
        AppLogger.Info("Main window launched");

        _cliServer = new CliPipeServer(RecordingManager, () => _currentSettings, ApplySettings);
    }

    private void OnTranscriptSegmentTranslated(object? sender, SegmentTranslationReadyEventArgs e) =>
        TranscriptViewModel.OnSegmentTranslated(e.SegmentId, e.TranslatedText);

    private static async System.Threading.Tasks.Task InitializeThemeAsync()
    {
        await ThemeHelper.InitializeAsync();
        TitleBarHelper.ApplySystemThemeToCaptionButtons(MainWindow, ThemeHelper.ActualTheme);
    }

    internal ITranslationService BuildTranslationService(AppSettings settings)
    {
        if (!settings.EnableTranslation)
            return new DisabledTranslationService();

        ITranslationProvider provider = settings.TranslationProvider switch
        {
            "google" => new GoogleTranslationProvider(_httpClientFactory, settings.GoogleTranslateApiKey),
            "deepl"  => new DeepLTranslationProvider(_httpClientFactory, settings.DeepLApiKey),
            _        => new DockerTranslationProvider(_httpClientFactory, settings.ApiUrl),
        };

        return new TranslationService(
            provider,
            targetLanguage: settings.TranslationTargetLanguage);
    }
}
