using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.Threading;
using System.Threading.Tasks;
using WhisperLive.Models;
using WhisperLive.Services;
using Windows.UI;

namespace WhisperLive.Views;

public sealed partial class LiveTranscriptPage : Page
{
    private CancellationTokenSource? _cts;

    public LiveTranscriptPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e) =>
        await CheckApiHealthAsync();

    private void OnUnloaded(object sender, RoutedEventArgs e) =>
        _cts?.Cancel();

    private static App CurrentApp => (App)Application.Current;

    // ── API health ────────────────────────────────────────────────────────────

    private async Task CheckApiHealthAsync()
    {
        SetStatusDot(Colors.Orange, "Checking API…");
        StartButton.IsEnabled = false;

        try
        {
            using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(3) };
            var response = await http.GetAsync("http://localhost:9000/health");
            SetApiReady(response.IsSuccessStatusCode);
        }
        catch
        {
            SetApiReady(false);
        }
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

    // ── Recording ─────────────────────────────────────────────────────────────

    private void OnStartClicked(object sender, RoutedEventArgs e)
    {
        _cts = new CancellationTokenSource();
        var options = new RecordingOptions(
            Language: "en",
            ChunkDurationSeconds: 5,
            ApiUrl: "http://localhost:9000",
            Model: "small");

        var app = CurrentApp;
        app.SubtitleService.SegmentAdded += OnSegmentAdded;
        _ = app.RecordingService.StartAsync(options, _cts.Token);
        _ = ConsumeChunksAsync(app, options, _cts.Token);

        MicIcon.Visibility = Visibility.Collapsed;
        Waveform.Visibility = Visibility.Visible;
        TranscriptScroller.Visibility = Visibility.Visible;
        StartButton.Visibility = Visibility.Collapsed;
        StopButton.Visibility = Visibility.Visible;
        ActionStatus.Text = "Listening…";

        WaveformStoryboard.Begin();
        DotPulseStoryboard.Begin();
        SetStatusDot(Color.FromArgb(255, 196, 43, 28), "Recording");

        App.CaptionOverlay?.ClearLines();
        App.CaptionOverlay?.AppWindow.Show();
    }

    private async void OnStopClicked(object sender, RoutedEventArgs e)
    {
        _cts?.Cancel();
        await CurrentApp.RecordingService.StopAsync();
        CurrentApp.SubtitleService.SegmentAdded -= OnSegmentAdded;

        WaveformStoryboard.Stop();
        DotPulseStoryboard.Stop();

        MicIcon.Visibility = Visibility.Visible;
        Waveform.Visibility = Visibility.Collapsed;
        StartButton.Visibility = Visibility.Visible;
        StopButton.Visibility = Visibility.Collapsed;
        ActionStatus.Text = "Stopped";
        App.CaptionOverlay?.AppWindow.Hide();

        SetApiReady(true);
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
                DispatcherQueue.TryEnqueue(() =>
                    ActionStatus.Text = $"Transcription error: {ex.Message}");
            }
            finally
            {
                try { System.IO.File.Delete(chunk.FilePath); } catch { }
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
            App.CaptionOverlay?.ShowSegment(seg.Text);
        });
    }
}
