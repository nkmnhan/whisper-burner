using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using WhisperLive.Infrastructure;
using WhisperLive.Models;
using WhisperLive.Services;
using Windows.System;
using Windows.UI;

namespace WhisperLive.Views;

public sealed partial class LiveTranscriptPage : Page
{
    private AppSettings _settings = new();
    private readonly ObservableCollection<string> _segments = [];
    private readonly ObservableCollection<AssistantMessage> _assistantMessages = [];
    private bool _isAsking;

    public LiveTranscriptPage()
    {
        InitializeComponent();
        TranscriptList.ItemsSource = _segments;
        AssistantChatList.ItemsSource = _assistantMessages;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private static App CurrentApp => (App)Application.Current;
    private static RecordingManager Manager => CurrentApp.RecordingManager;
    private static MeetingAssistantService Assistant => CurrentApp.MeetingAssistant;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        _settings = await AppSettings.LoadAsync();

        Manager.StateChanged += OnStateChanged;
        Manager.SegmentAdded += OnSegmentAdded;
        Assistant.NotesUpdated += OnNotesUpdated;

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
        Assistant.NotesUpdated -= OnNotesUpdated;
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
            Assistant.StartSession();
            await Manager.StartAsync(options);
        }
        else
        {
            await Manager.StopAsync();
            Assistant.EndSession();

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
        });
    }

    // ── Assistant panel ───────────────────────────────────────────────────────

    private void OnAssistantToggleClicked(object sender, RoutedEventArgs e) =>
        AssistantPanel.Visibility = AssistantPanel.Visibility == Visibility.Visible
            ? Visibility.Collapsed : Visibility.Visible;

    private void OnNotesTabClicked(object sender, RoutedEventArgs e)
    {
        NotesView.Visibility = Visibility.Visible;
        ChatView.Visibility = Visibility.Collapsed;
        NotesTabButton.Style = Application.Current.Resources["AccentButtonStyle"] as Style;
        AskTabButton.ClearValue(StyleProperty);
    }

    private void OnAskTabClicked(object sender, RoutedEventArgs e)
    {
        NotesView.Visibility = Visibility.Collapsed;
        ChatView.Visibility = Visibility.Visible;
        AskTabButton.Style = Application.Current.Resources["AccentButtonStyle"] as Style;
        NotesTabButton.ClearValue(StyleProperty);
    }

    private void OnNotesUpdated(object? sender, MeetingNotes notes)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            KeyPointsList.ItemsSource = notes.KeyPoints;
            DecisionsList.ItemsSource = notes.Decisions;
            ActionItemsList.ItemsSource = notes.ActionItems;

            KeyPointsSection.Visibility = ToVisibility(notes.KeyPoints.Count > 0);
            DecisionsSection.Visibility = ToVisibility(notes.Decisions.Count > 0);
            ActionItemsSection.Visibility = ToVisibility(notes.ActionItems.Count > 0);

            var hasNotes = notes.KeyPoints.Count > 0 || notes.Decisions.Count > 0 || notes.ActionItems.Count > 0;
            NotesEmptyLabel.Visibility = ToVisibility(!hasNotes);
            NotesUpdatedLabel.Text = hasNotes ? FormatUpdatedLabel(notes.GeneratedAt) : string.Empty;
        });
    }

    private static Visibility ToVisibility(bool value) => value ? Visibility.Visible : Visibility.Collapsed;

    private static string FormatUpdatedLabel(DateTimeOffset generatedAt)
    {
        var elapsed = DateTimeOffset.Now - generatedAt;
        if (elapsed < TimeSpan.FromMinutes(1))
            return "Updated just now";
        return elapsed < TimeSpan.FromHours(1)
            ? $"Updated {(int)elapsed.TotalMinutes}m ago"
            : $"Updated {(int)elapsed.TotalHours}h ago";
    }

    private async void OnRefreshNotesClicked(object sender, RoutedEventArgs e)
    {
        RefreshNotesButton.IsEnabled = false;
        try
        {
            await Assistant.RefreshNotesAsync();
        }
        finally
        {
            RefreshNotesButton.IsEnabled = true;
        }
    }

    private async void OnAssistantQuestionKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter)
            return;

        e.Handled = true;
        await SubmitQuestionAsync();
    }

    private async void OnSendQuestionClicked(object sender, RoutedEventArgs e) =>
        await SubmitQuestionAsync();

    private async Task SubmitQuestionAsync()
    {
        if (_isAsking)
            return;

        var question = AssistantQuestionBox.Text.Trim();
        if (question.Length == 0)
            return;

        _isAsking = true;
        AssistantQuestionBox.Text = string.Empty;
        AssistantQuestionBox.IsEnabled = false;
        SendQuestionButton.IsEnabled = false;

        _assistantMessages.Add(new AssistantMessage("You", question, DateTimeOffset.Now));

        try
        {
            var answer = await Assistant.AskAsync(question);
            _assistantMessages.Add(new AssistantMessage("Claude", answer, DateTimeOffset.Now));
        }
        finally
        {
            _isAsking = false;
            AssistantQuestionBox.IsEnabled = true;
            SendQuestionButton.IsEnabled = true;
            AssistantQuestionBox.Focus(FocusState.Programmatic);
        }
    }
}
