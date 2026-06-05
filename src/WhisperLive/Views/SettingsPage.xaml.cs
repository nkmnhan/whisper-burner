using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using System;
using WhisperLive.Helpers;
using WhisperLive.Infrastructure;

namespace WhisperLive.Views;

public sealed partial class SettingsPage : Page
{
    private AppSettings _settings = new();
    private bool _loaded;

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
