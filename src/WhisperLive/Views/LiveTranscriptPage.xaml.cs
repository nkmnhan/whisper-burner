using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using WhisperLive.Infrastructure;
using WhisperLive.Models;
using WhisperLive.Services;
using Windows.UI;

namespace WhisperLive.Views;

public sealed partial class LiveTranscriptPage : Page
{
    private CancellationTokenSource? _cts;
    private AppSettings _settings = new();
    private RecordingState _state = RecordingState.Idle;

    public LiveTranscriptPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        _settings = await AppSettings.LoadAsync();
        await CheckApiHealthAsync();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e) =>
        _cts?.Cancel();

    private static App CurrentApp => (App)Application.Current;

    // ── API health ────────────────────────────────────────────────────────────

    private async Task CheckApiHealthAsync()
    {
        SetStatusDot(Colors.Orange, "Checking API…");
        StartButton.IsEnabled = false;

        var healthy = await CurrentApp.TranscriptionClient.CheckHealthAsync(_settings.ApiUrl);
        SetApiReady(healthy);
    }

    private void SetApiReady(bool ready)
    {
        if (ready)
        {
            SetStatusDot(Color.FromArgb(255, 16, 124, 16), "API ready");
            StartButton.IsEnabled = true;
        }
        else
        {
            SetStatusDot(Color.FromArgb(255, 196, 43, 28), "API offline — start Docker first");
            StartButton.IsEnabled = false;
        }
    }

    private void SetStatusDot(Color color, string label)
    {
        StatusDot.Fill = new SolidColorBrush(color);
        StatusLabel.Text = label;
    }

    // ── State machine ─────────────────────────────────────────────────────────

    private void ApplyState(RecordingState state)
    {
        _state = state;
        switch (state)
        {
            case RecordingState.Idle:
                MicIcon.Visibility = Visibility.Visible;
                Waveform.Visibility = Visibility.Collapsed;
                TranscriptScroller.Visibility = Visibility.Collapsed;
                IdleActions.Visibility = Visibility.Visible;
                ActiveActions.Visibility = Visibility.Collapsed;
                NewSessionButton.Visibility = TranscriptText.Text.Length > 0
                    ? Visibility.Visible : Visibility.Collapsed;
                WaveformStoryboard.Stop();
                DotPulseStoryboard.Stop();
                App.CaptionOverlay?.AppWindow.Hide();
                break;

            case RecordingState.Recording:
                MicIcon.Visibility = Visibility.Collapsed;
                Waveform.Visibility = Visibility.Visible;
                TranscriptScroller.Visibility = Visibility.Visible;
                IdleActions.Visibility = Visibility.Collapsed;
                ActiveActions.Visibility = Visibility.Visible;
                PauseIcon.Glyph = "\uE769"; // Pause glyph
                ToolTipService.SetToolTip(PauseButton, "Pause recording");
                ShowOverlayButton.Visibility = Visibility.Collapsed;
                WaveformStoryboard.Begin();
                DotPulseStoryboard.Begin();
                SetStatusDot(Color.FromArgb(255, 196, 43, 28), "Recording");
                App.CaptionOverlay?.AppWindow.Show();
                break;

            case RecordingState.Paused:
                PauseIcon.Glyph = "\uE768"; // Play/Resume glyph
                WaveformStoryboard.Stop();
                DotPulseStoryboard.Stop();
                SetStatusDot(Colors.Orange, "Paused");
                App.CaptionOverlay?.UpdatePauseState(true);
                break;
        }
    }

    // ── Recording ─────────────────────────────────────────────────────────────

    private void OnStartClicked(object sender, RoutedEventArgs e)
    {
        _cts = new CancellationTokenSource();
        var options = new RecordingOptions(
            Language: _settings.Language,
            ChunkDurationSeconds: _settings.ChunkDurationSeconds,
            ApiUrl: _settings.ApiUrl,
            Model: _settings.Model);

        var app = CurrentApp;
        app.SubtitleService.StartSession();
        app.SubtitleService.SegmentAdded += OnSegmentAdded;
        _ = app.RecordingService.StartAsync(options, _cts.Token);
        _ = ConsumeChunksAsync(app, options, _cts.Token);

        App.CaptionOverlay?.ClearLines();
        App.CaptionOverlay?.SetLanguage(_settings.Language);
        App.CaptionOverlay?.UpdatePauseState(false);
        ApplyState(RecordingState.Recording);
    }

    private async void OnStopClicked(object sender, RoutedEventArgs e)
    {
        _cts?.Cancel();
        await CurrentApp.RecordingService.StopAsync();
        CurrentApp.SubtitleService.EndSession();
        CurrentApp.SubtitleService.SegmentAdded -= OnSegmentAdded;

        var savedFile = CurrentApp.SubtitleService.CurrentSessionPath is { } p
            ? $"Saved → {Path.GetFileName(p)}" : null;

        ApplyState(RecordingState.Idle);

        if (savedFile is not null)
            ActionStatus.Text = savedFile;

        await CheckApiHealthAsync();
    }

    private void OnPauseClicked(object sender, RoutedEventArgs e)
    {
        var svc = CurrentApp.RecordingService;
        if (_state == RecordingState.Recording)
        {
            _ = svc.PauseAsync();
            ApplyState(RecordingState.Paused);
            App.CaptionOverlay?.UpdatePauseState(true);
        }
        else if (_state == RecordingState.Paused)
        {
            _ = svc.ResumeAsync();
            ApplyState(RecordingState.Recording);
            App.CaptionOverlay?.UpdatePauseState(false);
        }
    }

    private void OnNewSessionClicked(object sender, RoutedEventArgs e)
    {
        TranscriptText.Text = string.Empty;
        TranscriptScroller.Visibility = Visibility.Collapsed;
        CurrentApp.SubtitleService.StartSession();
        App.CaptionOverlay?.ClearLines();
        NewSessionButton.Visibility = Visibility.Collapsed;
        ActionStatus.Text = string.Empty;
    }

    private void OnShowOverlayClicked(object sender, RoutedEventArgs e)
    {
        App.CaptionOverlay?.AppWindow.Show();
        ShowOverlayButton.Visibility = Visibility.Collapsed;
    }

    // ── Transcription loop ────────────────────────────────────────────────────

    private async Task ConsumeChunksAsync(App app, RecordingOptions options, CancellationToken ct)
    {
        await foreach (var chunk in app.RecordingService.Chunks.ReadAllAsync(ct))
        {
            try
            {
                var segments = await app.TranscriptionClient.TranscribeChunkAsync(chunk, options, ct);
                app.SubtitleService.AppendSegments(segments);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                AppLogger.Error(ex, "Transcription error on chunk");
                DispatcherQueue.TryEnqueue(() =>
                    ActionStatus.Text = $"Transcription error: {ex.Message}");
            }
            finally
            {
                try { File.Delete(chunk.FilePath); } catch { }
            }
        }
    }

    private void OnSegmentAdded(object? sender, SubtitleSegment seg)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            TranscriptText.Text += (TranscriptText.Text.Length > 0 ? " " : "") + seg.Text;
            TranscriptScroller.UpdateLayout();
            TranscriptScroller.ChangeView(null, TranscriptScroller.ScrollableHeight, null);

            // Show "Show Overlay" button if user has hidden the overlay
            if (_state == RecordingState.Recording &&
                App.CaptionOverlay?.AppWindow.IsVisible == false)
                ShowOverlayButton.Visibility = Visibility.Visible;

            App.CaptionOverlay?.ShowSegment(seg.Text);
        });
    }
}
