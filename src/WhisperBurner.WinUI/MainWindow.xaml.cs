using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WhisperBurner.WinUI.Views;

namespace WhisperBurner.WinUI;

public sealed partial class MainWindow : Window
{
    private readonly AppWindow _appWindow;

    public MainWindow()
    {
        InitializeComponent();

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        _appWindow = AppWindow.GetFromWindowId(Win32Interop.GetWindowIdFromWindow(hwnd));

        NavView.SelectedItem = NavView.MenuItems[0];
        ContentFrame.Navigate(typeof(RecordingPage));
    }

    public void MinimizeWindow() =>
        ((OverlappedPresenter)_appWindow.Presenter).Minimize();

    public void RestoreWindow() =>
        ((OverlappedPresenter)_appWindow.Presenter).Restore();

    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItemContainer?.Tag is not string tag)
            return;

        var pageType = tag switch
        {
            "Recording" => typeof(RecordingPage),
            "SessionReview" => typeof(SessionReviewPage),
            "Settings" => typeof(SettingsPage),
            _ => null
        };

        if (pageType is not null)
            ContentFrame.Navigate(pageType);
    }
}
