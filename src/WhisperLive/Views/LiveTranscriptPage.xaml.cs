using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace WhisperLive.Views;

public sealed partial class LiveTranscriptPage : Page
{
    public LiveTranscriptPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await CheckApiHealthAsync();
    }

    private async System.Threading.Tasks.Task CheckApiHealthAsync()
    {
        StatusLabel.Text = "Checking API…";
        StatusDot.Fill = new SolidColorBrush(Colors.Orange);

        try
        {
            using var http = new System.Net.Http.HttpClient { Timeout = System.TimeSpan.FromSeconds(3) };
            var response = await http.GetAsync("http://localhost:9000/");
            if (response.IsSuccessStatusCode)
            {
                SetApiReady(true);
            }
            else
            {
                SetApiReady(false);
            }
        }
        catch
        {
            SetApiReady(false);
        }
    }

    private void SetApiReady(bool ready)
    {
        if (ready)
        {
            StatusDot.Fill = new SolidColorBrush(Color.FromArgb(255, 16, 124, 16));
            StatusLabel.Text = "API ready";
            StartButton.IsEnabled = true;
        }
        else
        {
            StatusDot.Fill = new SolidColorBrush(Color.FromArgb(255, 196, 43, 28));
            StatusLabel.Text = "API offline — start Docker first";
            StartButton.IsEnabled = false;
        }
    }

    private void OnStartClicked(object sender, RoutedEventArgs e)
    {
        MicIcon.Visibility = Visibility.Collapsed;
        Waveform.Visibility = Visibility.Visible;
        TranscriptScroller.Visibility = Visibility.Visible;
        StartButton.Visibility = Visibility.Collapsed;
        StopButton.Visibility = Visibility.Visible;
        ActionStatus.Text = "Listening…";

        WaveformStoryboard.Begin();
        DotPulseStoryboard.Begin();
        StatusDot.Fill = new SolidColorBrush(Color.FromArgb(255, 196, 43, 28));
        StatusLabel.Text = "Recording";
    }

    private void OnStopClicked(object sender, RoutedEventArgs e)
    {
        WaveformStoryboard.Stop();
        DotPulseStoryboard.Stop();

        MicIcon.Visibility = Visibility.Visible;
        Waveform.Visibility = Visibility.Collapsed;
        StartButton.Visibility = Visibility.Visible;
        StopButton.Visibility = Visibility.Collapsed;
        ActionStatus.Text = "Stopped";

        SetApiReady(true);
    }
}
