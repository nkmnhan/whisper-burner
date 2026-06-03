using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WhisperBurner.WinUI.Infrastructure;
using WhisperBurner.WinUI.Models;
using WhisperBurner.WinUI.Overlay;
using WhisperBurner.WinUI.Services;

namespace WhisperBurner.WinUI.Views;

public sealed partial class RecordingPage : Page
{
    private readonly IRegionSelectionService _regionService = new RegionSelectionService();
    private readonly IRecordingService _recordingService = new RecordingService();
    private readonly ITranscriptionClient _transcriptionClient = new TranscriptionClient();
    private readonly ISubtitleService _subtitleService = new SubtitleService();
    private readonly ISessionRepository _sessionRepository = new SessionRepository();

    private SubtitleOverlayWindow? _overlay;
    private SessionManifest? _currentSession;
    private int _chunksSent;

    public RecordingPage()
    {
        InitializeComponent();
        AppLogger.Clear();
        AppLogger.Info("App started");
        _recordingService.AudioChunkReady += OnAudioChunkReady;
        _subtitleService.SegmentAdded += OnSegmentAdded;
        Loaded += async (_, _) => await RefreshApiStatusAsync();
        Unloaded += (_, _) =>
        {
            _recordingService.AudioChunkReady -= OnAudioChunkReady;
            _subtitleService.SegmentAdded -= OnSegmentAdded;
            _subtitleService.EndSession();
        };
    }

    private async Task RefreshApiStatusAsync()
    {
        var ok = await _transcriptionClient.IsAvailableAsync();
        AppLogger.Info($"API health check: {(ok ? "OK" : "OFFLINE")} — {AppSettings.Current.ApiUrl}");
        ApiDot.Fill = new SolidColorBrush(ok ? Colors.Green : Colors.Red);
        ApiStatusLabel.Text = ok ? "API ready" : "API offline";
        StartButton.IsEnabled = ok && _regionService.LastSelectedRegion is not null;
    }

    private async void SelectRegionButton_Click(object sender, RoutedEventArgs e)
    {
        SelectRegionButton.IsEnabled = false; // prevent double-click
        AppLogger.Info("Region selection started");
        CaptureRegion? region;
        try
        {
            region = await _regionService.SelectRegionAsync();
        }
        catch (Exception ex)
        {
            AppLogger.Error("Region selection threw", ex);
            StatusText.Text = $"Region selector failed: {ex.Message}";
            SelectRegionButton.IsEnabled = true;
            return;
        }
        SelectRegionButton.IsEnabled = true;
        if (region is null)
        {
            AppLogger.Info("Region selection cancelled");
            return;
        }
        AppLogger.Info($"Region selected: {region.Width}×{region.Height} at ({region.X},{region.Y})");
        RegionLabel.Text = $"{region.Width} × {region.Height}  at ({region.X}, {region.Y})";
        try
        {
            await RefreshApiStatusAsync();
        }
        catch (Exception ex)
        {
            AppLogger.Error("API status refresh failed after region selection", ex);
        }
        StatusText.Text = StartButton.IsEnabled
            ? "Region selected — press Start Recording."
            : "Region selected — start the Docker API first.";
    }

    private async void StartButton_Click(object sender, RoutedEventArgs e)
    {
        var region = _regionService.LastSelectedRegion;
        if (region is null) return;

        AppLogger.Info("Checking API before start...");
        if (!await _transcriptionClient.IsAvailableAsync())
        {
            AppLogger.Error("API offline — start aborted");
            StatusText.Text = "Cannot start — Docker API is offline. Run start-api-gpu.cmd first.";
            ApiDot.Fill = new SolidColorBrush(Colors.Red);
            ApiStatusLabel.Text = "API offline";
            return;
        }

        _chunksSent = 0;
        _subtitleService.Clear();

        var options = new RecordingOptions
        {
            Model = AppSettings.Current.Model,
            Language = AppSettings.Current.Language,
            ChunkDurationSeconds = AppSettings.Current.ChunkDurationSeconds,
            CaptureSystemAudio = AppSettings.Current.CaptureSystemAudio
        };
        AppLogger.Info($"Starting recording: model={options.Model} lang={options.Language} " +
                       $"chunk={options.ChunkDurationSeconds}s systemAudio={options.CaptureSystemAudio}");

        _currentSession = await _sessionRepository.CreateSessionAsync(region, options);
        AppLogger.Info($"Session created: {_currentSession.Id}");

        var sessionDir = _sessionRepository.GetSessionDirectory(_currentSession.Id);
        _subtitleService.StartSession(Path.Combine(sessionDir, "subtitles.ndjson"));

        _overlay = new SubtitleOverlayWindow(region);
        _overlay.StopRequested += async (_, _) => await StopRecordingAsync();
        _overlay.Activate();

        StartButton.IsEnabled = false;
        StartButton.Visibility = Visibility.Collapsed;
        StopButton.Visibility = Visibility.Visible;
        StatusText.Text = "Recording… (window minimised — use overlay Stop button)";

        try
        {
            await _recordingService.StartAsync(region, options);
            AppLogger.Info("Recording started successfully");
        }
        catch (Exception ex)
        {
            AppLogger.Error("Failed to start recording", ex);
            StatusText.Text = $"Recording failed to start: {ex.Message}";
            _overlay.Close();
            _overlay = null;
            StartButton.Visibility = Visibility.Visible;
            StopButton.Visibility = Visibility.Collapsed;
            StartButton.IsEnabled = true;
            return;
        }

        App.MainWindow?.MinimizeWindow();
    }

    private async void StopButton_Click(object sender, RoutedEventArgs e) =>
        await StopRecordingAsync();

    private async Task StopRecordingAsync()
    {
        if (!_recordingService.IsRecording) return;
        AppLogger.Info("Stopping recording...");

        await _recordingService.StopAsync();
        _overlay?.Close();
        _overlay = null;

        App.MainWindow?.RestoreWindow();

        StopButton.Visibility = Visibility.Collapsed;
        StartButton.Visibility = Visibility.Visible;
        StartButton.IsEnabled = _regionService.LastSelectedRegion is not null;

        var count = _subtitleService.Segments.Count;
        AppLogger.Info($"Recording stopped — {_chunksSent} chunks sent, {count} segments");
        StatusText.Text = $"Done — {count} subtitle segment{(count == 1 ? "" : "s")} captured.";

        _subtitleService.EndSession();

        if (_currentSession is not null && count > 0)
        {
            var dir = _sessionRepository.GetSessionDirectory(_currentSession.Id);
            await _subtitleService.WriteSrtAsync(Path.Combine(dir, "subtitles.srt"));
            await _sessionRepository.SaveManifestAsync(_currentSession);
            AppLogger.Info($"Session saved: {dir}");
            StatusText.Text += $"  Saved → {dir}";
            _currentSession = null;
        }

        await RefreshApiStatusAsync();
    }

    private async void OnAudioChunkReady(object? sender, AudioChunkInfo chunkInfo)
    {
        _chunksSent++;
        var fileSize = new FileInfo(chunkInfo.Path).Length;
        AppLogger.Info($"Chunk #{_chunksSent} ready: {Path.GetFileName(chunkInfo.Path)} " +
                       $"({fileSize:N0} bytes, offset={chunkInfo.OffsetSeconds:F1}s) — sending to API");

        try
        {
            using var stream = File.OpenRead(chunkInfo.Path);
            var segments = await _transcriptionClient.TranscribeChunkAsync(
                stream, AppSettings.Current.Model, AppSettings.Current.Language);

            // Shift chunk-relative timestamps to session-relative
            var offset = chunkInfo.OffsetSeconds;
            var offsetSegments = segments
                .Select(s => s with { Start = s.Start + offset, End = s.End + offset })
                .ToList();

            AppLogger.Info($"Chunk #{_chunksSent} transcribed: {offsetSegments.Count} segments, " +
                           $"text=\"{string.Join(" / ", offsetSegments.Select(s => s.Text.Trim()))}\"");

            _subtitleService.AppendSegments(offsetSegments);
            DispatcherQueue.TryEnqueue(() => _overlay?.SetListening());
        }
        catch (Exception ex)
        {
            AppLogger.Error($"Chunk #{_chunksSent} transcription failed", ex);
            DispatcherQueue.TryEnqueue(() =>
                StatusText.Text = $"Transcription error: {ex.Message}");
        }
        finally
        {
            try { File.Delete(chunkInfo.Path); } catch { }
        }
    }

    private void OnSegmentAdded(object? sender, SubtitleSegment segment)
    {
        AppLogger.Info($"Segment shown: \"{segment.Text.Trim()}\" [{segment.Start:F1}s→{segment.End:F1}s]");
        DispatcherQueue.TryEnqueue(() => _overlay?.ShowSegment(segment));
    }
}
