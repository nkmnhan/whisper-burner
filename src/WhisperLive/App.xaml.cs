using Microsoft.UI.Xaml;
using WhisperLive.Helpers;
using WhisperLive.Views;

namespace WhisperLive;

sealed partial class App : Application
{
    internal static MainWindow MainWindow { get; private set; } = null!;

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        MainWindow = new MainWindow();
        WindowHelper.TrackWindow(MainWindow);
        ThemeHelper.Initialize();
        MainWindow.Navigate(typeof(LiveTranscriptPage));
        MainWindow.Activate();
    }
}
