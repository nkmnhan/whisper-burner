using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WhisperBurner.WinUI.Views;

namespace WhisperBurner.WinUI;

public sealed partial class MainWindow : Window
{
    private readonly AppWindow _appWindow;

    public MainWindow()
    {
        Program.Trace("MainWindow() ctor: before InitializeComponent");
        try
        {
            InitializeComponent();
            Program.Trace("MainWindow() ctor: InitializeComponent done");
        }
        catch (Exception ex)
        {
            Program.Trace($"MainWindow() ctor: InitializeComponent THREW:\n{ex}");
            throw;
        }

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        _appWindow = AppWindow.GetFromWindowId(Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd));
        _appWindow.SetIcon("Assets\\AppIcon.ico");
        Program.Trace("MainWindow() ctor: icon set");

        SystemBackdrop = MicaController.IsSupported()
            ? new MicaBackdrop { Kind = MicaKind.Base }
            : new DesktopAcrylicBackdrop();
        Program.Trace($"MainWindow() ctor: backdrop={SystemBackdrop?.GetType().Name}");
        Program.Trace("MainWindow() ctor: done");
    }

    public void NavigateToRecording()
    {
        MainPivot.SelectedIndex = 0;
        if (RecordingFrame.Content is null)
            RecordingFrame.Navigate(typeof(RecordingPage));
    }

    public void MinimizeWindow() => ((OverlappedPresenter)_appWindow.Presenter).Minimize();
    public void RestoreWindow() => ((OverlappedPresenter)_appWindow.Presenter).Restore();

    private void MainPivot_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        switch (MainPivot.SelectedIndex)
        {
            case 0 when RecordingFrame.Content is null:
                RecordingFrame.Navigate(typeof(RecordingPage)); break;
            case 1 when SessionsFrame.Content is null:
                SessionsFrame.Navigate(typeof(SessionReviewPage)); break;
            case 2 when SettingsFrame.Content is null:
                SettingsFrame.Navigate(typeof(SettingsPage)); break;
        }
    }
}
