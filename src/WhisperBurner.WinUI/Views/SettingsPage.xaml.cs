using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WhisperBurner.WinUI.Infrastructure;

namespace WhisperBurner.WinUI.Views;

public sealed partial class SettingsPage : Page
{
    public SettingsPage()
    {
        InitializeComponent();
        Loaded += (_, _) => LoadSettings();
        // Mutual exclusion: system audio and mic are opposites
        CaptureSystemAudioToggle.Toggled += (_, _) =>
            CaptureMicToggle.IsOn = !CaptureSystemAudioToggle.IsOn;
        CaptureMicToggle.Toggled += (_, _) =>
            CaptureSystemAudioToggle.IsOn = !CaptureMicToggle.IsOn;
    }

    private void LoadSettings()
    {
        ApiUrlBox.Text = AppSettings.Current.ApiUrl;
        ModelCombo.SelectedIndex = AppSettings.Current.Model switch
        {
            "tiny" => 0, "base" => 1, "small" => 2,
            "medium" => 3, "large-v3" => 4, "turbo" => 5,
            _ => 2
        };
        ChunkDurationBox.Value = AppSettings.Current.ChunkDurationSeconds;
        CaptureSystemAudioToggle.IsOn = AppSettings.Current.CaptureSystemAudio;
        CaptureMicToggle.IsOn = !AppSettings.Current.CaptureSystemAudio;
        SessionsPathBox.Text = AppSettings.SessionsRoot;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        AppSettings.Current.ApiUrl = string.IsNullOrWhiteSpace(ApiUrlBox.Text)
            ? "http://localhost:5000"
            : ApiUrlBox.Text.Trim();

        AppSettings.Current.Model =
            ((ComboBoxItem?)ModelCombo.SelectedItem)?.Content?.ToString() ?? "small";

        AppSettings.Current.ChunkDurationSeconds = (int)ChunkDurationBox.Value;
        AppSettings.Current.CaptureSystemAudio = CaptureSystemAudioToggle.IsOn;
        AppSettings.Current.Save();

        SaveStatusText.Text = $"Saved to {AppSettings.DataRoot}";
    }
}
