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
        SelectTheme(_settings.Theme);

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

    private void OnThemeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_loaded) return;
        var tag = (string)((RadioButton)ThemeRadio.SelectedItem).Tag;
        _settings.Theme = tag;
        _ = _settings.SaveAsync();

        ThemeHelper.RootTheme = tag switch
        {
            "Light" => Microsoft.UI.Xaml.ElementTheme.Light,
            "Dark" => Microsoft.UI.Xaml.ElementTheme.Dark,
            _ => Microsoft.UI.Xaml.ElementTheme.Default,
        };
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

    private void SelectTheme(string theme)
    {
        foreach (RadioButton rb in ThemeRadio.Items)
        {
            if ((string)rb.Tag == theme)
            {
                ThemeRadio.SelectedItem = rb;
                return;
            }
        }
        ThemeRadio.SelectedIndex = 0;
    }
}
