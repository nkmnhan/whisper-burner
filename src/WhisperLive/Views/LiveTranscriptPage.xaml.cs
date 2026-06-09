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
    private readonly ObservableCollection<NotesBubble> _notesBubbles = [];
    private readonly List<NotesBubble> _notesHistory = [];
    private bool _isAsking;
    private int _displayOffset;

    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _notesTimer;
    private DateTimeOffset _notesLastRefresh;
    private const double NotesIntervalSeconds = 180.0;

    public LiveTranscriptPage()
    {
        InitializeComponent();
        TranscriptList.ItemsSource = _segments;
        AssistantChatList.ItemsSource = _assistantMessages;
        NotesChatList.ItemsSource = _notesBubbles;

        // Collapse ThinkingIndicator after its fade-out finishes, then stop the dots animation.
        HideThinkingStoryboard.Completed += (_, _) =>
        {
            ThinkingIndicator.Visibility = Visibility.Collapsed;
            ThinkingDotsStoryboard.Stop();
        };

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private static App CurrentApp => (App)Application.Current;
    private static RecordingManager Manager => CurrentApp.RecordingManager;
    private static MeetingAssistantService Assistant => CurrentApp.MeetingAssistant;
    private static TranscriptCorrectionService CorrectionService => CurrentApp.CorrectionService;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        _settings = await AppSettings.LoadAsync();

        _notesTimer = DispatcherQueue.CreateTimer();
        _notesTimer.Interval = TimeSpan.FromSeconds(1);
        _notesTimer.Tick += OnNotesTimerTick;

        Manager.StateChanged += OnStateChanged;
        Manager.SegmentAdded += OnSegmentAdded;
        Assistant.NotesUpdated += OnNotesUpdated;
        Assistant.NotesRefreshStarted += OnNotesRefreshStarted;
        CorrectionService.BatchCorrected += OnBatchCorrected;

        // Restore transcript that accumulated while we were away
        _segments.Clear();
        _displayOffset = 0;
        foreach (var s in Manager.GetRecentSegments())
            _segments.Add(s);

        if (string.IsNullOrEmpty(PreContextBox.Text))
            PreContextBox.Text = _settings.DefaultMeetingContext;

        ApplyState(Manager.State);

        if (Manager.State == RecordingState.Idle)
            await CheckApiHealthAsync();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _notesTimer?.Stop();
        Manager.StateChanged -= OnStateChanged;
        Manager.SegmentAdded -= OnSegmentAdded;
        Assistant.NotesUpdated -= OnNotesUpdated;
        Assistant.NotesRefreshStarted -= OnNotesRefreshStarted;
        CorrectionService.BatchCorrected -= OnBatchCorrected;
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

            var contextText = PreContextBox.Text.Trim();
            if (!string.IsNullOrEmpty(contextText))
            {
                _settings.RecentMeetingContexts.RemoveAll(p => p == contextText);
                _settings.RecentMeetingContexts.Insert(0, contextText);
                if (_settings.RecentMeetingContexts.Count > 10)
                    _settings.RecentMeetingContexts.RemoveRange(10, _settings.RecentMeetingContexts.Count - 10);
                _ = _settings.SaveAsync();
            }

            App.CaptionOverlay?.ClearLines();
            App.CaptionOverlay?.SetLanguage(_settings.Language);
            App.CaptionOverlay?.UpdatePauseState(false);
            Assistant.StartSession(PreContextBox.Text);
            CorrectionService.StartSession();
            _notesLastRefresh = DateTimeOffset.Now;
            NotesCountdownLabel.Visibility = Visibility.Visible;
            NotesEmptyLabel.Text = "Claude is listening — first notes in ~3 min";
            NotesWaitingRing.IsActive = true;
            NotesWaitingRing.Visibility = Visibility.Visible;
            _notesTimer?.Start();
            await Manager.StartAsync(options);
        }
        else
        {
            _notesTimer?.Stop();
            NotesCountdownLabel.Visibility = Visibility.Collapsed;
            NotesCountdownLabel.Text = string.Empty;
            NotesWaitingRing.IsActive = false;
            NotesWaitingRing.Visibility = Visibility.Collapsed;
            NotesEmptyLabel.Text = "Notes appear here — updated every 3 minutes";
            await Manager.StopAsync();
            Assistant.EndSession();
            CorrectionService.EndSession();

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
        _notesBubbles.Clear();
        _notesHistory.Clear();
        _displayOffset = 0;
        TranscriptList.Visibility = Visibility.Collapsed;
        NewSessionButton.Visibility = Visibility.Collapsed;
        NotesEmptyPanel.Visibility = Visibility.Visible;
        ActionStatus.Text = string.Empty;
        PreContextBox.Text = _settings.DefaultMeetingContext;
        App.CaptionOverlay?.ClearLines();
        CurrentApp.SubtitleService.StartSession();
    }

    private void OnShowOverlayClicked(object sender, RoutedEventArgs e)
    {
        App.CaptionOverlay?.AppWindow.Show();
        ShowOverlayButton.Visibility = Visibility.Collapsed;
    }

    private void OnContextHistoryClicked(object sender, RoutedEventArgs e)
    {
        var flyout = new MenuFlyout();
        var hasRecent = _settings.RecentMeetingContexts.Count > 0;
        var hasSaved = _settings.SavedMeetingContexts.Count > 0;

        if (!hasRecent && !hasSaved)
        {
            flyout.Items.Add(new MenuFlyoutItem { Text = "No prompts saved yet", IsEnabled = false });
        }
        else
        {
            if (hasRecent)
            {
                foreach (var prompt in _settings.RecentMeetingContexts)
                {
                    var display = prompt.Length > 60 ? prompt[..60] + "…" : prompt;
                    var item = new MenuFlyoutItem { Text = display, Icon = new FontIcon { Glyph = "" } };
                    var captured = prompt;
                    item.Click += (_, _) => PreContextBox.Text = captured;
                    flyout.Items.Add(item);
                }
            }

            if (hasSaved)
            {
                if (hasRecent) flyout.Items.Add(new MenuFlyoutSeparator());
                foreach (var saved in _settings.SavedMeetingContexts)
                {
                    var item = new MenuFlyoutItem { Text = saved.Name, Icon = new FontIcon { Glyph = "" } };
                    var captured = saved.Text;
                    item.Click += (_, _) => PreContextBox.Text = captured;
                    flyout.Items.Add(item);
                }
            }
        }

        flyout.ShowAt((FrameworkElement)sender);
    }

    private async void OnSaveContextClicked(object sender, RoutedEventArgs e)
    {
        var text = PreContextBox.Text.Trim();
        if (string.IsNullOrEmpty(text)) return;

        var nameBox = new TextBox
        {
            PlaceholderText = "e.g. Daily standup",
            Text = text.Length > 50 ? text[..50] : text,
            MaxLength = 60,
            Margin = new Microsoft.UI.Xaml.Thickness(0, 8, 0, 0),
        };

        var dialog = new ContentDialog
        {
            Title = "Save as preset",
            Content = nameBox,
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        var name = nameBox.Text.Trim();
        if (string.IsNullOrEmpty(name)) return;

        _settings.SavedMeetingContexts.RemoveAll(p => p.Name == name);
        _settings.SavedMeetingContexts.Add(new SavedPrompt(name, text));
        await _settings.SaveAsync();
    }

    // ── Segment display ───────────────────────────────────────────────────────

    private void OnSegmentAdded(object? sender, SubtitleSegment seg)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            _segments.Add(seg.Text);
            if (_segments.Count > 500)
            {
                _segments.RemoveAt(0);
                _displayOffset++;
            }

            if (Manager.State == RecordingState.Recording &&
                App.CaptionOverlay?.AppWindow.IsVisible == false)
                ShowOverlayButton.Visibility = Visibility.Visible;
        });
    }

    private void OnNotesTimerTick(Microsoft.UI.Dispatching.DispatcherQueueTimer sender, object args)
    {
        var elapsed = (DateTimeOffset.Now - _notesLastRefresh).TotalSeconds;
        var remaining = NotesIntervalSeconds - elapsed % NotesIntervalSeconds;
        var mins = (int)(remaining / 60);
        var secs = (int)(remaining % 60);
        NotesCountdownLabel.Text = $"Next in {mins}:{secs:D2}";
    }

    private void OnBatchCorrected(object? sender, IReadOnlyList<CorrectedSegment> corrections)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            var applied = 0;
            foreach (var correction in corrections)
            {
                var index = correction.OriginalId - 1 - _displayOffset;
                if (index >= 0 && index < _segments.Count)
                {
                    _segments[index] = correction.CorrectedText;
                    applied++;
                }
            }
            if (applied > 0)
                _ = ClearCorrectionStatusAsync(applied);
        });
    }

    private async Task ClearCorrectionStatusAsync(int applied)
    {
        ActionStatus.Text = $"AI corrected {applied} line{(applied == 1 ? "" : "s")}";
        await Task.Delay(4000);
        if (ActionStatus.Text.StartsWith("AI corrected"))
            ActionStatus.Text = string.Empty;
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

    private void OnNotesRefreshStarted(object? sender, EventArgs e)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            NotesRefreshRing.IsActive = true;
            NotesRefreshRing.Visibility = Visibility.Visible;
            NotesUpdatedLabel.Text = "Updating…";
        });
    }

    private void OnNotesUpdated(object? sender, MeetingNotes notes)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            _notesLastRefresh = DateTimeOffset.Now;
            NotesWaitingRing.IsActive = false;
            NotesWaitingRing.Visibility = Visibility.Collapsed;
            NotesRefreshRing.IsActive = false;
            NotesRefreshRing.Visibility = Visibility.Collapsed;

            var content = FormatNotesBubble(notes);
            if (string.IsNullOrWhiteSpace(content)) return;

            var bubble = new NotesBubble(content, notes.GeneratedAt);
            _notesHistory.Add(bubble);

            // Replace-in-place so the panel shows the current state, not a growing stack of duplicates
            if (_notesBubbles.Count > 0)
                _notesBubbles[0] = bubble;
            else
                _notesBubbles.Add(bubble);

            NotesEmptyPanel.Visibility = Visibility.Collapsed;
            NotesUpdatedLabel.Text = "Updated just now";
        });
    }

    private static Visibility ToVisibility(bool value) => value ? Visibility.Visible : Visibility.Collapsed;

    private static string FormatNotesBubble(MeetingNotes notes)
    {
        var sb = new System.Text.StringBuilder();
        AppendSection(sb, "Reasons", notes.Reasons);
        AppendSection(sb, "Goals", notes.Goals);
        AppendSection(sb, "Approaches", notes.Approaches);
        AppendSection(sb, "Decisions", notes.Decisions);
        return sb.ToString().TrimEnd();

        static void AppendSection(System.Text.StringBuilder b, string title, IReadOnlyList<string> items)
        {
            if (items.Count == 0) return;
            if (b.Length > 0) b.AppendLine();
            b.AppendLine(title);
            foreach (var item in items) b.AppendLine($"• {item}");
        }
    }

    private async void OnExpandNotesClicked(object sender, RoutedEventArgs e)
    {
        if (_notesHistory.Count == 0) return;

        var sb = new System.Text.StringBuilder();
        foreach (var bubble in _notesHistory)
        {
            sb.AppendLine($"── {bubble.TimeLabel} ──");
            sb.AppendLine(bubble.Content);
            sb.AppendLine();
        }

        var dialog = new ContentDialog
        {
            Title = "Meeting Notes",
            CloseButtonText = "Close",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
            MinWidth = 480,
        };

        var text = new TextBlock
        {
            Text = sb.ToString().TrimEnd(),
            TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap,
            IsTextSelectionEnabled = true,
            FontSize = 13,
            Margin = new Microsoft.UI.Xaml.Thickness(0, 0, 12, 0),
        };
        var scroll = new ScrollViewer { MaxHeight = 520, Content = text };
        dialog.Content = scroll;

        await dialog.ShowAsync();
    }

    private async void OnRefreshNotesClicked(object sender, RoutedEventArgs e)
    {
        RefreshNotesButton.IsEnabled = false;
        NotesRefreshRing.IsActive = true;
        NotesRefreshRing.Visibility = Visibility.Visible;
        NotesUpdatedLabel.Text = "Refreshing…";
        try
        {
            await Assistant.RefreshNotesAsync(force: true);
        }
        finally
        {
            NotesRefreshRing.IsActive = false;
            NotesRefreshRing.Visibility = Visibility.Collapsed;
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
        ShowThinking();

        _assistantMessages.Add(new AssistantMessage("You", question, DateTimeOffset.Now));

        try
        {
            var answer = await Assistant.AskAsync(question);
            _assistantMessages.Add(new AssistantMessage("Claude", answer, DateTimeOffset.Now));
        }
        finally
        {
            HideThinking();
            _isAsking = false;
            AssistantQuestionBox.IsEnabled = true;
            SendQuestionButton.IsEnabled = true;
            AssistantQuestionBox.Focus(FocusState.Programmatic);
        }
    }

    private void ShowThinking()
    {
        ThinkingIndicator.Visibility = Visibility.Visible;
        ThinkingDotsStoryboard.Begin();
        ShowThinkingStoryboard.Begin();
    }

    private void HideThinking()
    {
        // Fade out; Completed handler collapses Visibility and stops the dots storyboard.
        HideThinkingStoryboard.Begin();
    }
}
