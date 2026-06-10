using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.ObjectModel;
using System.Text;
using System.Threading.Tasks;
using WhisperLive.Infrastructure;
using WhisperLive.Models;
using WhisperLive.Services.Assistant;
using WhisperLive.Services.Audio;
using Windows.System;

namespace WhisperLive.Views;

public sealed partial class LiveTranscriptPage : Page
{
    private AppSettings _settings = new();
    private readonly ObservableCollection<string> _segments = [];
    private readonly ObservableCollection<AssistantMessage> _chatMessages = [];
    private readonly ObservableCollection<SessionSkill> _skills = [];
    private bool _isAsking;
    private int _displayOffset;

    public LiveTranscriptPage()
    {
        InitializeComponent();
        TranscriptList.ItemsSource = _segments;
        AssistantChatList.ItemsSource = _chatMessages;
        SuggestionStrip.ItemsSource = _skills;
        _chatMessages.CollectionChanged += (_, _) =>
        {
            var hasMessages = _chatMessages.Count > 0;
            ChatEmptyState.Visibility = hasMessages ? Visibility.Collapsed : Visibility.Visible;
            SuggestionStripScroller.Visibility = hasMessages ? Visibility.Visible : Visibility.Collapsed;
        };

        HideThinkingStoryboard.Completed += (_, _) =>
        {
            ThinkingIndicator.Visibility = Visibility.Collapsed;
            ThinkingDotsStoryboard.Stop();
        };

        PanelHideStoryboard.Completed += (_, _) =>
            AssistantPanel.Visibility = Visibility.Collapsed;

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private static App CurrentApp => (App)Application.Current;
    private static IRecordingManager Manager => CurrentApp.RecordingManager;
    private static ISessionAssistantService Assistant => CurrentApp.SessionAssistant;

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        _settings = await AppSettings.LoadAsync();

        Manager.StateChanged += OnStateChanged;
        Manager.SegmentAdded += OnSegmentAdded;

        _segments.Clear();
        _displayOffset = 0;
        foreach (var s in Manager.GetRecentSegments())
            _segments.Add(s);

        if (string.IsNullOrEmpty(PreContextBox.Text))
            PreContextBox.Text = _settings.DefaultSessionContext;

        AssistantToggleButton.Visibility = _settings.EnableAssistant
            ? Visibility.Visible : Visibility.Collapsed;

        _skills.Clear();
        foreach (var skill in SessionSkill.Defaults)
            _skills.Add(skill);
        foreach (var skill in _settings.CustomSkills)
            _skills.Add(skill);

        ApplyState(Manager.State);

        if (Manager.State == RecordingState.Idle)
            await CheckApiHealthAsync();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        Manager.StateChanged -= OnStateChanged;
        Manager.SegmentAdded -= OnSegmentAdded;
    }

    private async Task CheckApiHealthAsync()
    {
        SetStatusDot("StatusDotCautionBrush", "Checking API…");
        MainButton.IsEnabled = false;
        var healthy = await CurrentApp.TranscriptionClient.CheckHealthAsync(_settings.ApiUrl);
        SetApiReady(healthy);
    }

    private void SetApiReady(bool ready)
    {
        if (ready)
        {
            SetStatusDot("StatusDotReadyBrush", "API ready");
            MainButton.IsEnabled = true;
            ApiErrorBar.IsOpen = false;
        }
        else
        {
            SetStatusDot("StatusDotErrorBrush", "API offline");
            MainButton.IsEnabled = false;
            ApiErrorBar.IsOpen = true;
        }
    }

    private void SetStatusDot(string brushKey, string label)
    {
        StatusDot.Fill = (Brush)Application.Current.Resources[brushKey];
        StatusLabel.Text = label;
    }

    private void ShowActionStatus(string text)
    {
        ActionStatus.Text = text;
        StatusFadeInStoryboard.Begin();
    }

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
                AutomationProperties.SetName(MainButton, "Start recording");
                App.CaptionOverlay?.AppWindow.Hide();
                break;

            case RecordingState.Recording:
                IdlePlaceholder.Visibility = Visibility.Collapsed;
                TranscriptList.Visibility = Visibility.Visible;
                StartContent.Visibility = Visibility.Collapsed;
                WaveformInButton.Visibility = Visibility.Visible;
                PauseButton.Visibility = Visibility.Visible;
                NewSessionButton.Visibility = Visibility.Collapsed;
                PauseIcon.Glyph = "";
                AutomationProperties.SetName(PauseButton, "Pause recording");
                ToolTipService.SetToolTip(PauseButton, "Pause recording");
                WaveformStoryboard.Begin();
                DotPulseStoryboard.Begin();
                AutomationProperties.SetName(MainButton, "Stop recording");
                SetStatusDot("StatusDotErrorBrush", "Recording");
                App.CaptionOverlay?.AppWindow.Show();
                break;

            case RecordingState.Paused:
                PauseIcon.Glyph = "";
                AutomationProperties.SetName(PauseButton, "Resume recording");
                ToolTipService.SetToolTip(PauseButton, "Resume recording");
                WaveformStoryboard.Stop();
                DotPulseStoryboard.Stop();
                SetStatusDot("StatusDotCautionBrush", "Paused");
                App.CaptionOverlay?.UpdatePauseState(true);
                break;
        }
    }

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
                _settings.RecentSessionContexts.RemoveAll(p => p == contextText);
                _settings.RecentSessionContexts.Insert(0, contextText);
                if (_settings.RecentSessionContexts.Count > 10)
                    _settings.RecentSessionContexts.RemoveRange(10, _settings.RecentSessionContexts.Count - 10);
                _ = _settings.SaveAsync();
            }

            App.CaptionOverlay?.ClearLines();
            App.CaptionOverlay?.SetLanguage(_settings.Language);
            App.CaptionOverlay?.UpdatePauseState(false);

            if (_settings.EnableAssistant)
                Assistant.StartSession(PreContextBox.Text);

            await Manager.StartAsync(options);
        }
        else
        {
            await Manager.StopAsync();
            if (_settings.EnableAssistant)
                Assistant.EndSession();

            var saved = Manager.CurrentSessionPath is { } p
                ? $"Saved → {System.IO.Path.GetFileName(p)}" : null;
            if (saved is not null)
                ShowActionStatus(saved);

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
        _chatMessages.Clear();
        _displayOffset = 0;
        TranscriptList.Visibility = Visibility.Collapsed;
        NewSessionButton.Visibility = Visibility.Collapsed;
        ActionStatus.Text = string.Empty;
        PreContextBox.Text = _settings.DefaultSessionContext;
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
        var hasRecent = _settings.RecentSessionContexts.Count > 0;
        var hasSaved = _settings.SavedSessionContexts.Count > 0;

        if (!hasRecent && !hasSaved)
        {
            flyout.Items.Add(new MenuFlyoutItem { Text = "No prompts saved yet", IsEnabled = false });
        }
        else
        {
            if (hasRecent)
            {
                foreach (var prompt in _settings.RecentSessionContexts)
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
                if (hasRecent)
                    flyout.Items.Add(new MenuFlyoutSeparator());

                foreach (var saved in _settings.SavedSessionContexts)
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
            Margin = new Thickness(0, 8, 0, 0),
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

        _settings.SavedSessionContexts.RemoveAll(p => p.Name == name);
        _settings.SavedSessionContexts.Add(new SavedPrompt(name, text));
        await _settings.SaveAsync();
    }

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
            {
                ShowOverlayButton.Visibility = Visibility.Visible;
            }
        });
    }

    private void OnAssistantToggleChecked(object sender, RoutedEventArgs e) => ShowAssistantPanel();
    private void OnAssistantToggleUnchecked(object sender, RoutedEventArgs e) => HideAssistantPanel();

    private void ShowAssistantPanel()
    {
        AssistantBadge.Visibility = Visibility.Collapsed;
        AutomationProperties.SetName(AssistantToggleButton, "Assistant");
        AssistantPanel.Visibility = Visibility.Visible;
        PanelShowStoryboard.Begin();
    }

    private void HideAssistantPanel()
    {
        PanelHideStoryboard.Begin();
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
        if (_isAsking) return;

        var question = AssistantQuestionBox.Text.Trim();
        if (string.IsNullOrEmpty(question)) return;

        _isAsking = true;
        AssistantQuestionBox.Text = string.Empty;
        AssistantQuestionBox.IsEnabled = false;
        SendQuestionButton.IsEnabled = false;
        ShowThinking();

        _chatMessages.Add(new AssistantMessage("You", question, DateTimeOffset.Now));

        try
        {
            var answer = await Assistant.AskAsync(question);
            _chatMessages.Add(new AssistantMessage("Claude", answer, DateTimeOffset.Now));
        }
        catch (Exception ex)
        {
            _chatMessages.Add(new AssistantMessage("Error", $"Could not get a response. {ex.Message}", DateTimeOffset.Now));
        }
        finally
        {
            _isAsking = false;
            HideThinking();
            AssistantQuestionBox.IsEnabled = true;
            SendQuestionButton.IsEnabled = true;
            AssistantQuestionBox.Focus(FocusState.Programmatic);
        }
    }

    private async void OnSuggestionClicked(object sender, RoutedEventArgs e)
    {
        if (_isAsking) return;
        if (((Button)sender).Tag is not string prompt) return;

        AssistantQuestionBox.Text = prompt;
        await SubmitQuestionAsync();
    }

    private void OnClearChatClicked(object sender, RoutedEventArgs e)
    {
        _chatMessages.Clear();
        if (_isAsking) return;
        Assistant.EndSession();
        if (_settings.EnableAssistant)
            Assistant.StartSession(PreContextBox.Text);
    }

    private void OnCopyResponseClicked(object sender, RoutedEventArgs e)
    {
        if (((Button)sender).Tag is not string text) return;

        var dataPackage = new Windows.ApplicationModel.DataTransfer.DataPackage();
        dataPackage.SetText(text);
        Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dataPackage);
        ShowActionStatus("Copied to clipboard");
    }

    private async void OnSaveResponseClicked(object sender, RoutedEventArgs e)
    {
        if (((Button)sender).Tag is not string text) return;

        var fileName = $"assistant-{DateTimeOffset.Now:yyyyMMdd-HHmmss}.txt";
        await SaveAssistantTextAsync(fileName, text);
    }

    private async void OnSaveConversationClicked(object sender, RoutedEventArgs e)
    {
        if (_chatMessages.Count == 0)
        {
            ShowActionStatus("Nothing to save yet");
            return;
        }

        var builder = new StringBuilder();
        foreach (var message in _chatMessages)
        {
            builder.Append('[')
                .Append(message.Timestamp.ToLocalTime().ToString("HH:mm:ss"))
                .Append("] ")
                .Append(message.Role)
                .AppendLine();
            builder.AppendLine(message.Text);
            builder.AppendLine();
        }

        var fileName = $"assistant-chat-{DateTimeOffset.Now:yyyyMMdd-HHmmss}.txt";
        await SaveAssistantTextAsync(fileName, builder.ToString().TrimEnd());
    }

    private async Task SaveAssistantTextAsync(string fileName, string text)
    {
        var sessionDir = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "whisper.live", "sessions");
        System.IO.Directory.CreateDirectory(sessionDir);

        var path = System.IO.Path.Combine(sessionDir, fileName);
        await System.IO.File.WriteAllTextAsync(path, text);
        ShowActionStatus($"Saved → {fileName}");
    }

    private void ShowThinking()
    {
        ThinkingIndicator.Visibility = Visibility.Visible;
        ThinkingDotsStoryboard.Begin();
        ShowThinkingStoryboard.Begin();
    }

    private void HideThinking()
    {
        HideThinkingStoryboard.Begin();
    }
}
