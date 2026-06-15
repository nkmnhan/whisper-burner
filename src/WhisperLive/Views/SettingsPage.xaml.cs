using CommunityToolkit.WinUI.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using WhisperLive.Helpers;
using WhisperLive.Infrastructure;
using WhisperLive.Models;
using WhisperLive.Services.Assistant;
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
    private readonly ObservableCollection<SettingsCard> _skillCards = [];

    public SettingsPage()
    {
        InitializeComponent();
        AllowedPathsExpander.ItemsSource = _pathCards;
        ContextFolderExpander.ItemsSource = _contextFolderCards;
        SkillsExpander.ItemsSource = _skillCards;
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
        SelectComboItem(TranslationTargetBox, _settings.TranslationTargetLanguage);
        SelectComboItem(TranslationProviderBox, _settings.TranslationProvider == "whisper"
            ? "whisper (free)" : _settings.TranslationProvider);
        EnableTranslationToggle.IsOn = _settings.EnableTranslation;
        DeepLApiKeyBox.Text = _settings.DeepLApiKey;
        GoogleApiKeyBox.Text = _settings.GoogleTranslateApiKey;
        ApplyProviderVisibility(_settings.TranslationProvider);
        SelectThemeCombo(_settings.Theme);
        EnableAssistantToggle.IsOn = _settings.EnableAssistant;
        RebuildContextFolderItems();
        RebuildSkillItems();

        _allowedPaths.Clear();
        foreach (var p in _settings.AllowedReadPaths)
            _allowedPaths.Add(p);
        RebuildPathItems();

        DefaultContextBox.Text = _settings.DefaultSessionContext;

        try
        {
            var claudeMdPath = ClaudeCliProvider.GlobalClaudeMdPath;
            GlobalClaudeMdBox.Text = File.Exists(claudeMdPath)
                ? await File.ReadAllTextAsync(claudeMdPath)
                : ClaudeCliProvider.DefaultGlobalInstructions;
        }
        catch (Exception ex)
        {
            AppLogger.Warning(ex, "Could not load CLAUDE.md for settings editor");
        }

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

    private void OnGlobalClaudeMdChanged(object sender, TextChangedEventArgs e)
    {
        if (!_loaded) return;
        _ = SaveGlobalClaudeMdAsync(GlobalClaudeMdBox.Text);
    }

    private static async Task SaveGlobalClaudeMdAsync(string content)
    {
        try
        {
            var path = ClaudeCliProvider.GlobalClaudeMdPath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, content);
        }
        catch (Exception ex)
        {
            AppLogger.Warning(ex, "Could not save CLAUDE.md");
        }
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

    private void OnEnableTranslationToggled(object sender, RoutedEventArgs e)
    {
        if (!_loaded) return;
        _settings.EnableTranslation = EnableTranslationToggle.IsOn;
        _ = _settings.SaveAsync();
    }

    private void OnTranslationTargetChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_loaded) return;
        _settings.TranslationTargetLanguage = (string)((ComboBoxItem)TranslationTargetBox.SelectedItem).Content;
        _ = _settings.SaveAsync();
    }

    private void OnTranslationProviderChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_loaded) return;
        var raw = (string)((ComboBoxItem)TranslationProviderBox.SelectedItem).Content;
        // Map display label back to stored key
        var key = raw == "whisper (free)" ? "whisper" : raw;
        _settings.TranslationProvider = key;
        ApplyProviderVisibility(key);
        _ = _settings.SaveAsync();
    }

    private void ApplyProviderVisibility(string provider)
    {
        DockerProviderInfoCard.Visibility = provider == "whisper" ? Visibility.Visible : Visibility.Collapsed;
        DeepLApiKeyCard.Visibility        = provider == "deepl"  ? Visibility.Visible : Visibility.Collapsed;
        GoogleApiKeyCard.Visibility       = provider == "google" ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnDeepLApiKeyChanged(object sender, TextChangedEventArgs e)
    {
        if (!_loaded) return;
        _settings.DeepLApiKey = DeepLApiKeyBox.Text.Trim();
        _ = _settings.SaveAsync();
    }

    private void OnGoogleApiKeyChanged(object sender, TextChangedEventArgs e)
    {
        if (!_loaded) return;
        _settings.GoogleTranslateApiKey = GoogleApiKeyBox.Text.Trim();
        _ = _settings.SaveAsync();
    }

    private async void OnAddSkillClicked(object sender, RoutedEventArgs e)
    {
        var nameBox = new TextBox { PlaceholderText = "e.g. Key metrics", MaxLength = 40, Margin = new Thickness(0, 4, 0, 0) };
        var promptBox = new TextBox
        {
            PlaceholderText = "e.g. List all metrics and numbers mentioned in this session",
            AcceptsReturn = true,
            TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap,
            MaxHeight = 100,
            Margin = new Thickness(0, 8, 0, 0),
        };

        var panel = new StackPanel { Spacing = 0 };
        panel.Children.Add(new TextBlock { Text = "Name", Style = (Style)Application.Current.Resources["BodyTextBlockStyle"] });
        panel.Children.Add(nameBox);
        panel.Children.Add(new TextBlock { Text = "Prompt", Style = (Style)Application.Current.Resources["BodyTextBlockStyle"], Margin = new Thickness(0, 12, 0, 0) });
        panel.Children.Add(promptBox);

        var dialog = new ContentDialog
        {
            Title = "Add skill",
            Content = panel,
            PrimaryButtonText = "Add",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        var name = nameBox.Text.Trim();
        var prompt = promptBox.Text.Trim();
        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(prompt)) return;

        _settings.CustomSkills.RemoveAll(s => s.Name == name);
        _settings.CustomSkills.Add(new SessionSkill(name, prompt));
        await _settings.SaveAsync();
        RebuildSkillItems();
    }

    private async void OnRemoveSkillClicked(object sender, RoutedEventArgs e)
    {
        if (((Button)sender).Tag is not string name) return;
        _settings.CustomSkills.RemoveAll(s => s.Name == name);
        await _settings.SaveAsync();
        RebuildSkillItems();
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

    private void RebuildSkillItems()
    {
        _skillCards.Clear();
        foreach (var skill in _settings.CustomSkills)
        {
            var btn = new Button();
            ToolTipService.SetToolTip(btn, "Remove");
            btn.Content = new FontIcon { Glyph = "", FontSize = 12 };
            btn.Tag = skill.Name;
            btn.Click += OnRemoveSkillClicked;
            _skillCards.Add(new SettingsCard { Header = skill.Name, Description = skill.Prompt, Content = btn });
        }
    }

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
