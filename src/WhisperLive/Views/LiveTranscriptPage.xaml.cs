﻿using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using WhisperLive.Infrastructure;
using WhisperLive.Models;
using WhisperLive.Services.Assistant;
using WhisperLive.Services.Audio;
using WhisperLive.Services.Translation;

namespace WhisperLive.Views;

public sealed partial class LiveTranscriptPage : Page
{
    private AppSettings _settings = new();
    private readonly ObservableCollection<AssistantMessage> _chatMessages = [];
    private List<SessionSkill> _allSkills = [];
    private bool _isAsking;
    private CancellationTokenSource? _askCts;
    private CancellationTokenSource? _suggestDebounce;
    private CancellationTokenSource? _healthCheckCts;
    private bool _apiHealthy;
    private bool _suppressNextFocus;
    private readonly NotifyCollectionChangedEventHandler _onChatCollectionChanged;

    // Real-audio waveform — 16 bars driven by RMS amplitude from the loopback capture.
    private const int WaveBarCount = 16;
    private readonly ScaleTransform[] _barScales = new ScaleTransform[WaveBarCount];
    private readonly float[] _displayLevels = new float[WaveBarCount];

    // W-curve weights: outer bars slightly shorter, inner bars taller — natural spectrum look.
    private static readonly float[] WaveBarWeights =
        [0.45f, 0.55f, 0.70f, 0.85f, 0.95f, 1.00f, 0.90f, 0.80f,
         0.80f, 0.90f, 1.00f, 0.95f, 0.85f, 0.70f, 0.55f, 0.45f];

    // Gravity decay per frame: inner bars hold peak longer, outer bars fall faster.
    private static readonly float[] WaveBarDecay =
        [0.80f, 0.82f, 0.84f, 0.85f, 0.86f, 0.87f, 0.87f, 0.86f,
         0.86f, 0.87f, 0.87f, 0.86f, 0.85f, 0.84f, 0.82f, 0.80f];

    // Traveling-wave delays: each bar gets a random base delay and a unique oscillation
    // frequency/phase so bars drift independently — no fixed start position, organic motion.
    private readonly int[] _barBaseDelays = new int[WaveBarCount];
    private readonly float[] _barOscFreqs  = new float[WaveBarCount];
    private readonly float[] _barOscPhases = new float[WaveBarCount];
    private int _frameCount;

    // Ring buffer of recent RMS samples so each bar can read a time-delayed value.
    private const int RmsBufferSize = 24;
    private readonly float[] _rmsBuffer = new float[RmsBufferSize];
    private int _rmsHead;

    public LiveTranscriptPage()
    {
        InitializeComponent();
        TranscriptList.ItemsSource = CurrentApp.TranscriptViewModel.Segments;
        AssistantChatList.ItemsSource = _chatMessages;
        _onChatCollectionChanged = (_, _) =>
        {
            var hasMessages = _chatMessages.Count > 0;
            ChatEmptyState.Visibility = hasMessages ? Visibility.Collapsed : Visibility.Visible;
        };
        _chatMessages.CollectionChanged += _onChatCollectionChanged;

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
    private static IAssistantExportService AssistantExport => CurrentApp.AssistantExport;
    private static ITranslationService TranslationSvc => CurrentApp.TranslationService;

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        _settings = await AppSettings.LoadAsync();
        BuildWaveBars();

        // Rebuild TranslationService with real settings; App re-wires SegmentTranslated to VM.
        CurrentApp.ApplySettings(_settings);

        Manager.StateChanged += OnStateChanged;
        Manager.ApiStalled += OnApiStalled;
        Manager.RecordingFaulted += OnRecordingFaulted;
        Manager.AudioLevelChanged += OnAudioLevelChanged;
        CurrentApp.TranscriptViewModel.Segments.CollectionChanged += OnSegmentsChanged;
        if (App.CaptionOverlay is { } overlayOnLoad) overlayOnLoad.Hidden += OnOverlayHidden;

        if (string.IsNullOrEmpty(PreContextBox.Text))
            PreContextBox.Text = _settings.DefaultSessionContext;

        AssistantToggleButton.Visibility = _settings.EnableAssistant
            ? Visibility.Visible : Visibility.Collapsed;

        if (_settings.EnableTranslation)
        {
            TranslationChip.Visibility = Visibility.Visible;
            TranslationChipLabel.Text = TranslationSvc.TargetLanguage;
        }

        _allSkills = [.. SessionSkill.Defaults, .. _settings.CustomSkills];

        ApplyState(Manager.State);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        SetHealthPolling(false);
        _suggestDebounce?.Cancel();
        _suggestDebounce?.Dispose();
        _suggestDebounce = null;
        Manager.StateChanged -= OnStateChanged;
        Manager.ApiStalled -= OnApiStalled;
        Manager.RecordingFaulted -= OnRecordingFaulted;
        Manager.AudioLevelChanged -= OnAudioLevelChanged;
        CurrentApp.TranscriptViewModel.Segments.CollectionChanged -= OnSegmentsChanged;
        if (App.CaptionOverlay is { } overlayOnUnload) overlayOnUnload.Hidden -= OnOverlayHidden;
        _chatMessages.CollectionChanged -= _onChatCollectionChanged;
        _askCts?.Cancel();
        _askCts?.Dispose();
        _askCts = null;
    }

    private void OnSegmentsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add &&
            Manager.State == RecordingState.Recording &&
            App.CaptionOverlay?.AppWindow.IsVisible == false)
        {
            ShowOverlayButton.Visibility = Visibility.Visible;
        }
    }

    private void OnOverlayHidden(object? sender, EventArgs e)
    {
        if (Manager.State == RecordingState.Recording)
            ShowOverlayButton.Visibility = Visibility.Visible;
    }

    private void SetHealthPolling(bool active)
    {
        _healthCheckCts?.Cancel();
        _healthCheckCts?.Dispose();
        _healthCheckCts = active ? new CancellationTokenSource() : null;
        if (active)
        {
            // Preserve the last known health state so the Start button remains enabled
            // immediately when transitioning from a successful recording session.
            // The first health check below corrects it if the API has since gone offline.
            _ = PollHealthAsync(_healthCheckCts!.Token);
        }
    }

    private async Task PollHealthAsync(CancellationToken ct)
    {
        await CheckApiHealthAsync();
        while (!ct.IsCancellationRequested)
        {
            // Poll at 5 s when unhealthy (fast recovery), 30 s when healthy (reduce log noise).
            var delay = _apiHealthy ? TimeSpan.FromSeconds(30) : TimeSpan.FromSeconds(5);
            try { await Task.Delay(delay, ct); }
            catch (OperationCanceledException) { break; }
            if (!ct.IsCancellationRequested)
                await CheckApiHealthAsync();
        }
    }

    private async Task CheckApiHealthAsync()
    {
        if (!_apiHealthy)
        {
            SetStatusDot("StatusDotCautionBrush", "Checking API…");
            MainButton.IsEnabled = false;
        }
        _apiHealthy = await CurrentApp.TranscriptionClient.CheckHealthAsync(_settings.ApiUrl);
        SetApiReady(_apiHealthy);
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

    private void BuildWaveBars()
    {
        WaveformInButton.Children.Clear();
        var barBrush = (Brush)Application.Current.Resources["TextOnAccentFillColorPrimaryBrush"];
        var rng = new Random();
        for (int i = 0; i < WaveBarCount; i++)
        {
            // Random starting delay (0–7 frames) and unique slow-oscillation frequency/phase
            // so each bar drifts independently — wave start position is never the same.
            _barBaseDelays[i] = rng.Next(0, 8);
            _barOscFreqs[i]   = 0.04f + rng.NextSingle() * 0.08f; // ~0.04–0.12 rad/frame
            _barOscPhases[i]  = rng.NextSingle() * MathF.Tau;

            var scale = new ScaleTransform { CenterY = 18, ScaleY = 0.06 };
            _barScales[i] = scale;
            WaveformInButton.Children.Add(new Border
            {
                Width = 3,
                Height = 18,
                CornerRadius = new CornerRadius(1.5),
                Background = barBrush,
                RenderTransform = scale,
                VerticalAlignment = VerticalAlignment.Center,
            });
        }
    }

    private void OnAudioLevelChanged(object? sender, float rms)
    {
        // Arrives on the audio capture thread. Do NOT touch the ring-buffer fields here — they are
        // read on the UI thread inside the lambda below, so all access must stay on one thread to
        // avoid a data race. Marshal the raw sample over and mutate everything UI-side.
        DispatcherQueue.TryEnqueue(() =>
        {
            _rmsBuffer[_rmsHead] = rms;
            _rmsHead = (_rmsHead + 1) % RmsBufferSize;
            int frame = ++_frameCount;

            for (int i = 0; i < WaveBarCount; i++)
            {
                // Each bar's effective delay drifts sinusoidally over time using its own
                // unique frequency and phase — bars never peak in a fixed sequence, so the
                // wave pattern is organic and has no repeating start position.
                float drift = MathF.Sin(frame * _barOscFreqs[i] + _barOscPhases[i]) * 4f;
                int delay = Math.Clamp(_barBaseDelays[i] + (int)drift, 0, RmsBufferSize - 1);
                int delayedIdx = (_rmsHead - 1 - delay + RmsBufferSize * 2) % RmsBufferSize;
                var target = _rmsBuffer[delayedIdx] * WaveBarWeights[i];
                _displayLevels[i] = MathF.Max(target, _displayLevels[i] * WaveBarDecay[i]);
                _barScales[i].ScaleY = MathF.Max(0.06f, _displayLevels[i]);
            }
        });
    }

    private void ShowActionStatus(string text)
    {
        ActionStatus.Text = text;
        StatusFadeInStoryboard.Begin();
    }

    private void OnStateChanged(object? sender, RecordingState state) =>
        DispatcherQueue.TryEnqueue(() => ApplyState(state));

    private void OnApiStalled(object? sender, EventArgs e) =>
        DispatcherQueue.TryEnqueue(() =>
        {
            if (Manager.State == RecordingState.Recording)
                SetStatusDot("StatusDotCautionBrush", "API not responding — restart Docker");
        });

    private void OnRecordingFaulted(object? sender, string message) =>
        DispatcherQueue.TryEnqueue(() =>
        {
            // The manager also transitions to Idle (OnStateChanged → ApplyState), which resets the
            // UI; here we just surface why recording stopped.
            ShowActionStatus(message);
            if (_settings.EnableAssistant)
                Assistant.EndSession();
        });

    private void ApplyState(RecordingState state)
    {
        SetHealthPolling(state == RecordingState.Idle);
        switch (state)
        {
            case RecordingState.Idle:
                IdlePlaceholder.Visibility = Visibility.Visible;
                TranscriptList.Visibility = Visibility.Collapsed;
                StartContent.Visibility = Visibility.Visible;
                WaveformInButton.Visibility = Visibility.Collapsed;
                PauseButton.Visibility = Visibility.Collapsed;
                ShowOverlayButton.Visibility = Visibility.Collapsed;
                NewSessionButton.Visibility = CurrentApp.TranscriptViewModel.Segments.Count > 0
                    ? Visibility.Visible : Visibility.Collapsed;
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
                PauseIcon.Glyph = "\uE769";
                AutomationProperties.SetName(PauseButton, "Pause recording");
                ToolTipService.SetToolTip(PauseButton, "Pause recording");
                DotPulseStoryboard.Begin();
                AutomationProperties.SetName(MainButton, "Stop recording");
                SetStatusDot("StatusDotErrorBrush", "Recording");
                App.CaptionOverlay?.AppWindow.Show();
                break;

            case RecordingState.Paused:
                PauseIcon.Glyph = "\uE768"; // Play (Resume)
                AutomationProperties.SetName(PauseButton, "Resume recording");
                ToolTipService.SetToolTip(PauseButton, "Resume recording");
                DotPulseStoryboard.Stop();
                SetStatusDot("StatusDotCautionBrush", "Paused");
                break;
        }
    }

    private async void OnMainButtonClicked(object sender, RoutedEventArgs e)
    {
        if (Manager.State == RecordingState.Idle)
        {
            var globalContext = _settings.DefaultSessionContext?.Trim() ?? "";
            var sessionContext = PreContextBox.Text.Trim();
            var initialPrompt = string.Join("\n",
                new[] { globalContext, sessionContext }.Where(s => s.Length > 0));

            var options = new RecordingOptions(
                Language: _settings.Language,
                ChunkDurationSeconds: _settings.ChunkDurationSeconds,
                ApiUrl: _settings.ApiUrl,
                Model: _settings.Model,
                InitialPrompt: initialPrompt.Length > 0 ? initialPrompt : null,
                Task: _settings.EnableTranslation && _settings.TranslationProvider == "whisper"
                    ? "translate" : "transcribe");

            if (!string.IsNullOrEmpty(sessionContext))
            {
                _settings.RecentSessionContexts.RemoveAll(p => p == sessionContext);
                _settings.RecentSessionContexts.Insert(0, sessionContext);
                if (_settings.RecentSessionContexts.Count > 10)
                    _settings.RecentSessionContexts.RemoveRange(10, _settings.RecentSessionContexts.Count - 10);
                _ = _settings.SaveAsync();
            }

            CurrentApp.TranscriptViewModel.Clear();
            App.CaptionOverlay?.SetLanguage(
                _settings.Language,
                _settings.EnableTranslation ? TranslationSvc.TargetLanguage : null);

            try
            {
                await Manager.StartAsync(options);
            }
            catch (RecordingStartException ex)
            {
                // Manager already rolled back to Idle; just surface why and leave the UI restartable.
                AppLogger.Warning(ex, "Recording failed to start");
                ShowActionStatus(ex.Message);
                return;
            }

            // Start the assistant session only after capture is confirmed running.
            if (_settings.EnableAssistant)
                Assistant.StartSession(sessionContext);
        }
        else
        {
            await Manager.StopAsync();
            if (_settings.EnableAssistant)
                Assistant.EndSession();

            // Resolve any rows still waiting for translation — show original text cleanly.
            CurrentApp.TranscriptViewModel.FinalizeSession();

            var saved = Manager.CurrentSessionPath is { } p
                ? $"Saved → {System.IO.Path.GetFileName(p)}" : null;
            if (saved is not null)
                ShowActionStatus(saved);
        }
    }

    private async void OnPauseClicked(object sender, RoutedEventArgs e)
    {
        if (Manager.State == RecordingState.Recording)
        {
            await Manager.PauseAsync();
        }
        else if (Manager.State == RecordingState.Paused)
        {
            await Manager.ResumeAsync();
        }
    }

    private void OnNewSessionClicked(object sender, RoutedEventArgs e)
    {
        CurrentApp.TranscriptViewModel.Clear();
        _chatMessages.Clear();
        TranscriptList.Visibility = Visibility.Collapsed;
        NewSessionButton.Visibility = Visibility.Collapsed;
        ActionStatus.Text = string.Empty;
        PreContextBox.Text = _settings.DefaultSessionContext;
        // SubtitleService.StartSession() is owned by RecordingManager.StartAsync — no call needed here.
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
                    var item = new MenuFlyoutItem { Text = display, Icon = new FontIcon { Glyph = "" } };
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
                    var item = new MenuFlyoutItem { Text = saved.Name, Icon = new FontIcon { Glyph = "" } };
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

    private async Task SubmitQuestionAsync(string? overrideQuestion = null)
    {
        if (_isAsking) return;

        var question = (overrideQuestion ?? AssistantQuestionBox.Text).Trim();
        if (string.IsNullOrEmpty(question)) return;

        _isAsking = true;
        AssistantQuestionBox.Text = string.Empty;
        AssistantQuestionBox.ItemsSource = null; // force-close the dropdown
        AssistantQuestionBox.IsEnabled = false;
        ShowThinking();

        _askCts?.Cancel();
        _askCts?.Dispose();
        _askCts = new CancellationTokenSource();
        var askToken = _askCts.Token;

        _chatMessages.Add(new AssistantMessage("You", question, DateTimeOffset.Now));

        try
        {
            // Force onto the thread pool so Process.Start and context-building
            // never block the UI thread — all UI updates already happened above.
            var answer = await Task.Run(
                () => Assistant.AskAsync(question, new AskOptions(IncludeBufferedTranscript: true), askToken),
                askToken);
            _chatMessages.Add(new AssistantMessage("Claude", answer, DateTimeOffset.Now));
        }
        catch (OperationCanceledException)
        {
            _chatMessages.Add(new AssistantMessage("Claude", "⚠ Request cancelled.", DateTimeOffset.Now));
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
            _suppressNextFocus = true;
            AssistantQuestionBox.Focus(FocusState.Programmatic);
        }
    }

    private void OnAssistantQuestionBoxGotFocus(object sender, RoutedEventArgs e)
    {
        if (_suppressNextFocus) { _suppressNextFocus = false; return; }
        if (_isAsking) return;
        if (string.IsNullOrEmpty(AssistantQuestionBox.Text))
            AssistantQuestionBox.ItemsSource = _allSkills;
    }

    private async void OnAssistantSuggestTextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs e)
    {
        if (e.Reason != AutoSuggestionBoxTextChangeReason.UserInput) return;
        if (_isAsking) return;

        _suggestDebounce?.Cancel();
        _suggestDebounce?.Dispose();
        _suggestDebounce = new CancellationTokenSource();
        var cts = _suggestDebounce;

        try { await Task.Delay(250, cts.Token); }
        catch (OperationCanceledException) { return; }

        var query = sender.Text.Trim();
        if (string.IsNullOrEmpty(query))
        {
            sender.ItemsSource = _allSkills;
            return;
        }

        var tokens = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var results = _allSkills.Where(s => tokens.All(t =>
            s.Name.Contains(t, StringComparison.OrdinalIgnoreCase) ||
            s.Prompt.Contains(t, StringComparison.OrdinalIgnoreCase))).ToList();

        sender.ItemsSource = results.Count > 0 ? (IEnumerable<SessionSkill>)results : null;
    }

    private void OnAssistantSuggestionChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs e)
    {
        if (e.SelectedItem is SessionSkill skill)
            sender.Text = skill.Name;
    }

    private async void OnAssistantQuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs e)
    {
        var question = e.ChosenSuggestion is SessionSkill skill ? skill.Prompt : e.QueryText;
        await SubmitQuestionAsync(question);
    }

    private async void OnSuggestionClicked(object sender, RoutedEventArgs e)
    {
        if (_isAsking) return;
        if (((Button)sender).Tag is not string prompt) return;
        await SubmitQuestionAsync(prompt);
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

        try
        {
            var fileName = $"assistant-{DateTimeOffset.Now:yyyyMMdd-HHmmss}.txt";
            var path = await AssistantExport.SaveAsync(fileName, text);
            ShowActionStatus($"Saved → {System.IO.Path.GetFileName(path)}");
        }
        catch (Exception ex)
        {
            AppLogger.Warning(ex, "Failed to save assistant response");
            ShowActionStatus("Save failed — check folder permissions");
        }
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

        try
        {
            var fileName = $"assistant-chat-{DateTimeOffset.Now:yyyyMMdd-HHmmss}.txt";
            var path = await AssistantExport.SaveAsync(fileName, builder.ToString().TrimEnd());
            ShowActionStatus($"Saved → {System.IO.Path.GetFileName(path)}");
        }
        catch (Exception ex)
        {
            AppLogger.Warning(ex, "Failed to save assistant conversation");
            ShowActionStatus("Save failed — check folder permissions");
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
        HideThinkingStoryboard.Begin();
    }
}
