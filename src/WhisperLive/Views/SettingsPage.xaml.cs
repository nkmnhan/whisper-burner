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

    public SettingsPage()
    {
        InitializeComponent();
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
        ContextFolderLabel.Text = string.IsNullOrWhiteSpace(_settings.ContextFolderPath)
            ? "Not set" : _settings.ContextFolderPath;

        _allowedPaths.Clear();
        foreach (var p in _settings.AllowedReadPaths)
            _allowedPaths.Add(p);
        RebuildPathItems();

        _loaded = true;
    }

    // ── Handlers ──────────────────────────────────────────────────────────────

    private void OnApiUrlChanged(object sender, TextChangedEventArgs e)
    {
        if (!_loaded) return;
        _settings.ApiUrl = ApiUrlBox.Text.Trim();
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

    private async void OnBrowseContextFolderClicked(object sender, RoutedEventArgs e)
    {
        if (WindowHelper.GetWindowForElement(this) is not Window window) return;

        var picker = new FolderPicker { SuggestedStartLocation = PickerLocationId.ComputerFolder };
        picker.FileTypeFilter.Add("*");
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(window));

        var folder = await picker.PickSingleFolderAsync();
        if (folder is null) return;

        _settings.ContextFolderPath = folder.Path;
        ContextFolderLabel.Text = folder.Path;
        await _settings.SaveAsync();
    }

    private async void OnClearContextFolderClicked(object sender, RoutedEventArgs e)
    {
        _settings.ContextFolderPath = null;
        ContextFolderLabel.Text = "Not set";
        await _settings.SaveAsync();
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

        var picker = new FolderPicker { SuggestedStartLocation = PickerLocationId.ComputerFolder };
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

    private void RebuildPathItems()
    {
        AllowedPathsExpander.Items.Clear();
        AllowedPathsExpander.Items.Add(MakeBuiltInCard(BuiltInDataPath));
        foreach (var path in _allowedPaths)
            AllowedPathsExpander.Items.Add(MakeUserCard(path));
    }

    private static SettingsCard MakeBuiltInCard(string path) => new()
    {
        Header = path,
        Description = "Built-in — always allowed (sessions, settings, logs)",
        Content = new FontIcon { Glyph = "", FontSize = 14 },
    };

    private SettingsCard MakeUserCard(string path)
    {
        var btn = new Button();
        ToolTipService.SetToolTip(btn, "Remove");
        btn.Content = new FontIcon { Glyph = "", FontSize = 12 };
        btn.Tag = path;
        btn.Click += OnRemovePathClicked;
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
