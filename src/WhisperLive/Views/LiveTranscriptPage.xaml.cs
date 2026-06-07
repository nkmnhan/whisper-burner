using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using WhisperLive.Infrastructure;
using WhisperLive.Models;
using WhisperLive.Services;
using Windows.UI;

namespace WhisperLive.Views;

public sealed partial class LiveTranscriptPage : Page
{
    private AppSettings _settings = new();
    private readonly ObservableCollection<string> _segments = [];

    public LiveTranscriptPage()
    {
        InitializeComponent();
        TranscriptList.ItemsSource = _segments;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private static App CurrentApp => (App)Application.Current;
    private static RecordingManager Manager => CurrentApp.RecordingManager;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        _settings = await AppSettings.LoadAsync();

        Manager.StateChanged += OnStateChanged;
        Manager.SegmentAdded += OnSegmentAdded;

        // Restore transcript that accumulated while we were away
        _segments.Clear();
        foreach (var s in Manager.GetRecentSegments())
            _segments.Add(s);

        ApplyState(Manager.State);

        if (Manager.State == RecordingState.Idle)
            await CheckApiHealthAsync();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        Manager.StateChanged -= OnStateChanged;
        Manager.SegmentAdded -= OnSegmentAdded;
    }

    // ── API health ────────────────────────────────────────────────────────────

    private async Task CheckApiHealthAsync()
    {
        SetStatusDot(Colors.Orange, "Checking API…");
        MainButton.IsEnabled = false;
        var healthy = await CurrentApp.TranscriptionClient.CheckHealthAsync(_settings.ApiUrl);
        SetApiReady(healthy);
    }

    private void SetApiReady(bool ready)
    {
        if (ready)
        {
            SetStatusDot(Color.FromArgb(255, 16, 124, 16), "API ready");
            MainButton.IsEnabled = true;
        }
        else
        {
            SetStatusDot(Color.FromArgb(255, 196, 43, 28), "API offline — start Docker first");
            MainButton.IsEnabled = false;
        }
    }

    private void SetStatusDot(Color color, string label)
    {
        StatusDot.Fill = new SolidColorBrush(color);
        StatusLabel.Text = label;
    }

    // ── State ─────────────────────────────────────────────────────────────────

    private void OnStateChanged(object? sender, RecordingState state) =>
        DispatcherQueue.TryEnqueue(() => ApplyState(state));

    private void ApplyState(RecordingState state)
    {
        switch (state)
        {
            case RecordingState.Idle:
                IdlePlaceholder.Visibility = Visibility.Visible;
                TranscriptList.Visibility = Visibility.Collapsed;
                StartContent.Visibility = Visibility.Visible;
                WaveformInButton.Visibility = Visibility.Collapsed;
                PauseButton.Visibility = Visibility.Collapsed;
                ShowOverlayButton.Visibility = Visibility.Collapsed;
                NewSessionButton.Visibility = _segments.Count > 0
                    ? Visibility.Visible : Visibility.Collapsed;
                WaveformStoryboard.Stop();
                DotPulseStoryboard.Stop();
                App.CaptionOverlay?.AppWindow.Hide();
                break;

            case RecordingState.Recording:
                IdlePlaceholder.Visibility = Visibility.Collapsed;
                TranscriptList.Visibility = Visibility.Visible;
                StartContent.Visibility = Visibility.Collapsed;
                WaveformInButton.Visibility = Visibility.Visible;
                PauseButton.Visibility = Visibility.Visible;
                NewSessionButton.Visibility = Visibility.Collapsed;
                PauseIcon.Glyph = ""; // Pause
                ToolTipService.SetToolTip(PauseButton, "Pause recording");
                WaveformStoryboard.Begin();
                DotPulseStoryboard.Begin();
                SetStatusDot(Color.FromArgb(255, 196, 43, 28), "Recording");
                App.CaptionOverlay?.AppWindow.Show();
                break;

            case RecordingState.Paused:
                PauseIcon.Glyph = ""; // Resume
                WaveformStoryboard.Stop();
                DotPulseStoryboard.Stop();
                SetStatusDot(Colors.Orange, "Paused");
                App.CaptionOverlay?.UpdatePauseState(true);
                break;
        }
    }

    // ── Controls ─────────────────────────────────────────────────────────────

    private async void OnMainButtonClicked(object sender, RoutedEventArgs e)
    {
        if (Manager.State == RecordingState.Idle)
        {
            var options = new RecordingOptions(
                Language: _settings.Language,
                ChunkDurationSeconds: _settings.ChunkDurationSeconds,
                ApiUrl: _settings.ApiUrl,
                Model: _settings.Model);

            App.CaptionOverlay?.ClearLines();
            App.CaptionOverlay?.SetLanguage(_settings.Language);
            App.CaptionOverlay?.UpdatePauseState(false);
            await Manager.StartAsync(options);
        }
        else
        {
            await Manager.StopAsync();

            var saved = Manager.CurrentSessionPath is { } p
                ? $"Saved → {System.IO.Path.GetFileName(p)}" : null;
            if (saved is not null)
                ActionStatus.Text = saved;

            await CheckApiHealthAsync();
        }
    }

    private async void OnPauseClicked(object sender, RoutedEventArgs e)
    {
        if (Manager.State == RecordingState.Recording)
        {
            await Manager.PauseAsync();
            App.CaptionOverlay?.UpdatePauseState(true);
        }
        else if (Manager.State == RecordingState.Paused)
        {
            await Manager.ResumeAsync();
            App.CaptionOverlay?.UpdatePauseState(false);
        }
    }

    private void OnNewSessionClicked(object sender, RoutedEventArgs e)
    {
        _segments.Clear();
        TranscriptList.Visibility = Visibility.Collapsed;
        NewSessionButton.Visibility = Visibility.Collapsed;
        ActionStatus.Text = string.Empty;
        App.CaptionOverlay?.ClearLines();
        CurrentApp.SubtitleService.StartSession();
    }

    private void OnShowOverlayClicked(object sender, RoutedEventArgs e)
    {
        App.CaptionOverlay?.AppWindow.Show();
        ShowOverlayButton.Visibility = Visibility.Collapsed;
    }

    // ── Segment display ───────────────────────────────────────────────────────

    private void OnSegmentAdded(object? sender, SubtitleSegment seg)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            _segments.Add(seg.Text);
            if (_segments.Count > 500)
                _segments.RemoveAt(0);

            if (Manager.State == RecordingState.Recording &&
                App.CaptionOverlay?.AppWindow.IsVisible == false)
                ShowOverlayButton.Visibility = Visibility.Visible;

            App.CaptionOverlay?.ShowSegment(seg);
        });
    }
}
