using CommunityToolkit.WinUI.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using System;
using System.Collections.ObjectModel;
using System.IO;
using WhisperLive.Helpers;
using WhisperLive.Infrastructure;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace WhisperLive.Views;

public sealed partial class SettingsPage : Page
{
    private static readonly string BuiltInDataPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "whisper.live");

    private AppSettings _settings = new();
    private bool _loaded;
    private readonly ObservableCollection<string> _allowedPaths = [];
    private readonly ObservableCollection<SettingsCard> _pathCards = [];
    private readonly ObservableCollection<SettingsCard> _contextFolderCards = [];

    public SettingsPage()
    {
        InitializeComponent();
        AllowedPathsExpander.ItemsSource = _pathCards;
        ContextFolderExpander.ItemsSource = _contextFolderCards;
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        _settings = await AppSettings.LoadAsync();
        _loaded = false;

        ApiUrlBox.Text = _settings.ApiUrl;
        ChunkSlider.Value = _settings.ChunkDurationSeconds;
        ChunkLabel.Text = $"{_settings.ChunkDurationSeconds} s";
        SelectComboItem(ModelBox, _settings.Model);
        SelectComboItem(LanguageBox, _settings.Language);
        SelectThemeCombo(_settings.Theme);
        EnableAssistantToggle.IsOn = _settings.EnableAssistant;
        AllowFullTranscriptToggle.IsOn = _settings.AllowFullTranscriptPrompts;
        RebuildContextFolderItems();

        _allowedPaths.Clear();
        foreach (var p in _settings.AllowedReadPaths)
            _allowedPaths.Add(p);
        RebuildPathItems();

        DefaultContextBox.Text = _settings.DefaultSessionContext;

        _loaded = true;
    }

    // ── Handlers ──────────────────────────────────────────────────────────────

    private void OnApiUrlChanged(object sender, TextChangedEventArgs e)
    {
        if (!_loaded) return;
        _settings.ApiUrl = ApiUrlBox.Text.Trim();
        _ = _settings.SaveAsync();
    }

    private void OnDefaultContextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_loaded) return;
        _settings.DefaultSessionContext = DefaultContextBox.Text;
        _ = _settings.SaveAsync();
    }

    private void OnModelChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_loaded) return;
        _settings.Model = (string)((ComboBoxItem)ModelBox.SelectedItem).Content;
        _ = _settings.SaveAsync();
    }

    private void OnChunkDurationChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (!_loaded) return;
        _settings.ChunkDurationSeconds = (int)ChunkSlider.Value;
        ChunkLabel.Text = $"{_settings.ChunkDurationSeconds} s";
        _ = _settings.SaveAsync();
    }

    private void OnLanguageChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_loaded) return;
        _settings.Language = (string)((ComboBoxItem)LanguageBox.SelectedItem).Content;
        _ = _settings.SaveAsync();
    }

    private async void OnAddContextFolderClicked(object sender, RoutedEventArgs e)
    {
        if (WindowHelper.GetWindowForElement(this) is not Window window) return;

        var picker = new FolderPicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
        picker.FileTypeFilter.Add("*");
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(window));

        var folder = await picker.PickSingleFolderAsync();
        if (folder is null) return;
        if (_settings.ContextFolderPaths.Contains(folder.Path)) return;

        _settings.ContextFolderPaths.Add(folder.Path);
        await _settings.SaveAsync();
        RebuildContextFolderItems();
        RebuildPathItems();
    }

    private void OnEnableAssistantToggled(object sender, RoutedEventArgs e)
    {
        if (!_loaded) return;
        _settings.EnableAssistant = EnableAssistantToggle.IsOn;
        _ = _settings.SaveAsync();
    }

    private void OnAllowFullTranscriptToggled(object sender, RoutedEventArgs e)
    {
        if (!_loaded) return;
        _settings.AllowFullTranscriptPrompts = AllowFullTranscriptToggle.IsOn;
        _ = _settings.SaveAsync();
    }

    private async void OnRemoveContextFolderClicked(object sender, RoutedEventArgs e)
    {
        if (((Button)sender).Tag is not string path) return;

        _settings.ContextFolderPaths.Remove(path);
        await _settings.SaveAsync();
        RebuildContextFolderItems();
        RebuildPathItems();
    }

    // Gallery-pattern: null-safe cast, no _loaded guard needed (guard is the null check itself)
    private void OnThemeChanged(object sender, SelectionChangedEventArgs e)
    {
        if ((ThemeCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() is not string tag) return;

        _settings.Theme = tag;
        _ = _settings.SaveAsync();

        ThemeHelper.RootTheme = tag switch
        {
            "Light" => ElementTheme.Light,
            "Dark" => ElementTheme.Dark,
            _ => ElementTheme.Default,
        };

        // Gallery: update caption button colours after theme change (workaround for SDK bug)
        if (WindowHelper.GetWindowForElement(this) is Window w)
            TitleBarHelper.ApplySystemThemeToCaptionButtons(w, ThemeHelper.ActualTheme);
    }

    private async void OnAddFolderClicked(object sender, RoutedEventArgs e)
    {
        if (WindowHelper.GetWindowForElement(this) is not Window window) return;

        var picker = new FolderPicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
        picker.FileTypeFilter.Add("*");
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(window));

        var folder = await picker.PickSingleFolderAsync();
        if (folder is null) return;
        if (_allowedPaths.Contains(folder.Path)) return;

        _allowedPaths.Add(folder.Path);
        _settings.AllowedReadPaths.Add(folder.Path);
        await _settings.SaveAsync();
        RebuildPathItems();
    }

    private async void OnAddFileClicked(object sender, RoutedEventArgs e)
    {
        if (WindowHelper.GetWindowForElement(this) is not Window window) return;

        var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.ComputerFolder };
        picker.FileTypeFilter.Add("*");
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(window));

        var file = await picker.PickSingleFileAsync();
        if (file is null) return;
        if (_allowedPaths.Contains(file.Path)) return;

        _allowedPaths.Add(file.Path);
        _settings.AllowedReadPaths.Add(file.Path);
        await _settings.SaveAsync();
        RebuildPathItems();
    }

    private async void OnRemovePathClicked(object sender, RoutedEventArgs e)
    {
        if (((Button)sender).Tag is not string path) return;

        _allowedPaths.Remove(path);
        _settings.AllowedReadPaths.Remove(path);
        await _settings.SaveAsync();
        RebuildPathItems();
    }


    // ── Path list ─────────────────────────────────────────────────────────────

    private void RebuildContextFolderItems()
    {
        _contextFolderCards.Clear();
        foreach (var path in _settings.ContextFolderPaths)
            _contextFolderCards.Add(MakeUserCard(path, OnRemoveContextFolderClicked));
    }

    private void RebuildPathItems()
    {
        _pathCards.Clear();
        _pathCards.Add(MakeBuiltInCard(BuiltInDataPath, "Built-in — sessions, settings, logs"));
        foreach (var path in _settings.ContextFolderPaths)
            _pathCards.Add(MakeBuiltInCard(path, "Context folder — set in Context folders above"));
        foreach (var path in _allowedPaths)
            _pathCards.Add(MakeUserCard(path, OnRemovePathClicked));
    }

    private static SettingsCard MakeBuiltInCard(string path, string description) => new()
    {
        Header = path,
        Description = description,
        Content = new FontIcon { Glyph = "", FontSize = 14 },
    };

    private static SettingsCard MakeUserCard(string path, RoutedEventHandler removeHandler)
    {
        var btn = new Button();
        ToolTipService.SetToolTip(btn, "Remove");
        btn.Content = new FontIcon { Glyph = "", FontSize = 12 };
        btn.Tag = path;
        btn.Click += removeHandler;
        return new SettingsCard { Header = path, Content = btn };
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static void SelectComboItem(ComboBox box, string value)
    {
        foreach (ComboBoxItem item in box.Items)
        {
            if ((string)item.Content == value)
            {
                box.SelectedItem = item;
                return;
            }
        }
        if (box.Items.Count > 0)
            box.SelectedIndex = 0;
    }

    private void SelectThemeCombo(string theme)
    {
        foreach (ComboBoxItem item in ThemeCombo.Items)
        {
            if ((string?)item.Tag == theme)
            {
                ThemeCombo.SelectedItem = item;
                return;
            }
        }
        ThemeCombo.SelectedIndex = 2; // Default
    }
}
