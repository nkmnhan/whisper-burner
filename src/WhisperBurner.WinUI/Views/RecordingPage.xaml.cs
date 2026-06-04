using System.Collections.ObjectModel;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using WhisperBurner.WinUI.Infrastructure;
using WhisperBurner.WinUI.Models;
using WhisperBurner.WinUI.Overlay;
using WhisperBurner.WinUI.Services.Audio;
using WhisperBurner.WinUI.Services.Video;

namespace WhisperBurner.WinUI.Views;

public sealed partial class RecordingPage : Page
{
    private readonly IRecordingService      _recordingService;
    private readonly ITranscriptionClient   _transcriptionClient;
    private readonly ISubtitleService       _subtitleService;
    private readonly ISessionRepository     _sessionRepository;

    private SessionManifest? _currentSession;
    private string?          _savedSessionDir;
    private int              _chunksSent;
    private bool             _isStopped = true;
    private SubtitleOverlayWindow? _overlay;
    private Storyboard?      _waveformStoryboard;
    private Storyboard?      _pulseDotStoryboard;
    private CancellationTokenSource? _sessionCts;
    private readonly ObservableCollection<SubtitleSegment> _transcriptItems = new();
    private const int MaxTranscriptLines = 10;

    public RecordingPage()
    {
        InitializeComponent();
        var app = (App)Application.Current;
        _recordingService    = app.RecordingService;
        _transcriptionClient = app.TranscriptionClient;
        _subtitleService     = app.SubtitleService;
        _sessionRepository   = app.SessionRepository;

        TranscriptRepeater.ItemsSource = _transcriptItems;
        AppLogger.Clear();
        AppLogger.Info("App started");
        _recordingService.AudioChunkReady = OnAudioChunkReady;
        // SegmentAdded registered per-session in StartRecordingAsync to avoid race on stop
        Loaded += async (_, _) =>
        {
            try
            {
                _waveformStoryboard = (Storyboard)Resources["WaveformStoryboard"];
                _pulseDotStoryboard = (Storyboard)Resources["PulseDotStoryboard"];
                await RefreshApiStatusAsync();
            }
            catch (Exception ex)
            {
                AppLogger.Error("RecordingPage Loaded failed", ex);
                StatusText.Text = $"Init error: {ex.GetType().Name}: {ex.Message}";
            }
        };
        Unloaded += (_, _) =>
        {
            _recordingService.AudioChunkReady = null;
            _subtitleService.SegmentAdded    -= OnSegmentAdded;
            _subtitleService.EndSession();
            _sessionCts?.Cancel();
            _sessionCts?.Dispose();
        };
    }

    private async Task RefreshApiStatusAsync()
    {
        var health = await _transcriptionClient.GetHealthAsync();
        AppLogger.Info($"API health: {(health.IsOnline ? "OK" : "OFFLINE")} model={health.LoadedModel ?? "-"}");
        ApiDot.Fill = new SolidColorBrush(health.IsOnline ? Colors.Green : Colors.Red);
        var modelTag = health.LoadedModel is { } m ? $" [{m}]" : "";
        ApiStatusLabel.Text  = health.IsOnline ? $"API ready{modelTag}" : "API offline";
        ModelLabel.Text      = $"· {AppSettings.Current.Model}";
        StartButton.IsEnabled = health.IsOnline;
        if (health.IsOnline) StatusText.Text = "Ready — press Start Recording  (Ctrl+R)";
    }

    private async void StartButton_Click(object sender, RoutedEventArgs e) =>
        await StartRecordingAsync();

    private async void StopButton_Click(object sender, RoutedEventArgs e) =>
        await StopRecordingAsync();

    private void CtrlR_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs e)
    {
        e.Handled = true;
        if (_recordingService.IsRecording)
            _ = StopRecordingAsync();
        else if (StartButton.IsEnabled)
            _ = StartRecordingAsync();
    }

    private async Task StartRecordingAsync()
    {
        var health = await _transcriptionClient.GetHealthAsync();
        if (!health.IsOnline)
        {
            StatusText.Text = "Cannot start — Docker API is offline. Run start-api-gpu.cmd first.";
            ApiDot.Fill     = new SolidColorBrush(Colors.Red);
            ApiStatusLabel.Text = "API offline";
            StartButton.IsEnabled = false;
            return;
        }

        _chunksSent = 0;
        _isStopped  = false;
        _sessionCts?.Dispose();
        _sessionCts = new CancellationTokenSource();
        _subtitleService.SegmentAdded += OnSegmentAdded;  // re-register for this session
        _subtitleService.Clear();
        _transcriptItems.Clear();
        SavedBanner.Visibility = Visibility.Collapsed;

        var options = new RecordingOptions
        {
            Model                = AppSettings.Current.Model,
            Language             = AppSettings.Current.Language,
            ChunkDurationSeconds = AppSettings.Current.ChunkDurationSeconds,
            CaptureSystemAudio   = AppSettings.Current.CaptureSystemAudio
        };

        _currentSession = await _sessionRepository.CreateSessionAsync(null, options);
        var sessionDir = _sessionRepository.GetSessionDirectory(_currentSession.Id);
        _subtitleService.StartSession(Path.Combine(sessionDir, "subtitles.ndjson"));

        StartButton.IsEnabled   = false;
        StartButton.Visibility  = Visibility.Collapsed;
        StopButton.Visibility   = Visibility.Visible;
        MicIcon.Visibility      = Visibility.Collapsed;
        WaveformPanel.Visibility = Visibility.Visible;
        LiveTranscriptPanel.Visibility = Visibility.Visible;
        _waveformStoryboard?.Begin();
        ApiDot.Fill         = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 196, 43, 28));
        ApiStatusLabel.Text = "Recording";
        _pulseDotStoryboard?.Begin();
        StatusText.Text = "Listening…";

        // Open overlay window at bottom center of primary screen
        try
        {
            var screen = Microsoft.UI.Windowing.DisplayArea.Primary;
            var r = screen.WorkArea;
            var region = new CaptureRegion(r.X, r.Y, r.Width, r.Height);
            _overlay = new SubtitleOverlayWindow(region);
            _overlay.StopRequested += (_, _) => _ = StopRecordingAsync();
            _overlay.Activate();
            _overlay.SetListening();
        }
        catch (Exception ex) { AppLogger.Error("Overlay failed to open", ex); }

        try { await _recordingService.StartAsync(null, options); }
        catch (Exception ex)
        {
            AppLogger.Error("Failed to start recording", ex);
            StatusText.Text = $"Recording failed to start: {ex.Message}";
            ResetToIdleState();
        }
    }

    private async Task StopRecordingAsync()
    {
        if (!_recordingService.IsRecording) return;
        _isStopped = true;                        // guard: no more segment UI updates
        _sessionCts?.Cancel();
        _subtitleService.SegmentAdded -= OnSegmentAdded;  // unregister before stopping
        await _recordingService.StopAsync();
        ResetToIdleState();

        var count = _subtitleService.Segments.Count;
        StatusText.Text = $"Done — {count} segment{(count == 1 ? "" : "s")} captured.";
        _subtitleService.EndSession();

        if (_currentSession is not null && count > 0)
        {
            var dir = _sessionRepository.GetSessionDirectory(_currentSession.Id);
            await _subtitleService.WriteSrtAsync(Path.Combine(dir, "subtitles.srt"));
            await _sessionRepository.SaveManifestAsync(_currentSession);
            _savedSessionDir = dir;
            _currentSession  = null;
            ShowSavedBanner(dir, count);
        }

        await RefreshApiStatusAsync();
    }

    private void ShowSavedBanner(string dir, int count)
    {
        SavedBannerText.Text   = $"{count} segment{(count == 1 ? "" : "s")} saved → {Path.GetFileName(dir)}";
        SavedBanner.Visibility = Visibility.Visible;
        _ = Task.Delay(8000).ContinueWith(_ =>
            DispatcherQueue.TryEnqueue(() => SavedBanner.Visibility = Visibility.Collapsed));
    }

    private void OpenFolderBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_savedSessionDir is { } dir && Directory.Exists(dir))
            System.Diagnostics.Process.Start("explorer.exe", dir);
    }

    private void DismissBanner_Click(object sender, RoutedEventArgs e) =>
        SavedBanner.Visibility = Visibility.Collapsed;

    private void ResetToIdleState()
    {
        // Clear items BEFORE hiding the panel — prevents ItemsRepeater crash
        // when DispatcherQueue callbacks fire after the panel is collapsed
        _transcriptItems.Clear();

        // Close overlay safely
        try { _overlay?.Close(); } catch { }
        _overlay = null;

        _waveformStoryboard?.Stop();
        _pulseDotStoryboard?.Stop();
        ApiDotScale.ScaleX = 1;
        ApiDotScale.ScaleY = 1;
        WaveformPanel.Visibility       = Visibility.Collapsed;
        LiveTranscriptPanel.Visibility = Visibility.Collapsed;
        MicIcon.Visibility        = Visibility.Visible;
        StopButton.Visibility     = Visibility.Collapsed;
        StartButton.Visibility    = Visibility.Visible;
        StartButton.IsEnabled     = true;
    }

    private async Task OnAudioChunkReady(AudioChunkInfo chunkInfo)
    {
        _chunksSent++;
        var ct = _sessionCts?.Token ?? CancellationToken.None;
        try
        {
            using var stream   = File.OpenRead(chunkInfo.Path);
            var segments       = await _transcriptionClient.TranscribeChunkAsync(
                stream, AppSettings.Current.Model, AppSettings.Current.Language, ct);
            var offset         = chunkInfo.OffsetSeconds;
            var offsetSegments = segments
                .Select(s => s with { Start = s.Start + offset, End = s.End + offset })
                .ToList();
            _subtitleService.AppendSegments(offsetSegments);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            AppLogger.Error("Chunk transcription failed", ex);
            DispatcherQueue.TryEnqueue(() => StatusText.Text = $"Error: {ex.Message}");
        }
        finally { try { File.Delete(chunkInfo.Path); } catch { } }
    }

    private void OnSegmentAdded(object? sender, SubtitleSegment segment)
    {
        if (_isStopped) return;  // guard against race after StopRecordingAsync
        _overlay?.ShowSegment(segment);  // forward to overlay (thread-safe internally)
        DispatcherQueue.TryEnqueue(() =>
        {
            if (_isStopped) return;  // double-check on UI thread
            _transcriptItems.Add(segment);
            while (_transcriptItems.Count > MaxTranscriptLines)
                _transcriptItems.RemoveAt(0);
            TranscriptScroller.ChangeView(null, double.MaxValue, null, disableAnimation: true);
            StatusText.Text = segment.Text.Trim();
        });
    }
}

