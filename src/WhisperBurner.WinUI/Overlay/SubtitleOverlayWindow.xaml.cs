using Microsoft.UI;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using WhisperBurner.WinUI.Models;
using Windows.Graphics;

namespace WhisperBurner.WinUI.Overlay;

public sealed partial class SubtitleOverlayWindow : Window
{
    private const int OverlayWidth = 720;
    private const int OverlayHeight = 130;
    private const int DragHandleHeight = 22;

    public event EventHandler? StopRequested;

    private readonly AppWindow _appWindow;
    private int _segmentCount;

    public SubtitleOverlayWindow(CaptureRegion region)
    {
        InitializeComponent();

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        _appWindow = AppWindow.GetFromWindowId(Win32Interop.GetWindowIdFromWindow(hwnd));

        var presenter = (OverlappedPresenter)_appWindow.Presenter;
        presenter.IsAlwaysOnTop = true;
        presenter.IsResizable = true;
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        presenter.SetBorderAndTitleBar(false, false);

        // Position at bottom-centre of captured region
        int x = region.X + (region.Width - OverlayWidth) / 2;
        int y = region.Y + region.Height - OverlayHeight - 20;
        _appWindow.MoveAndResize(new RectInt32(Math.Max(0, x), Math.Max(0, y), OverlayWidth, OverlayHeight));

        // Native WinUI 3 drag — InputNonClientPointerSource replaces WM_NCLBUTTONDOWN P/Invoke
        var inputSource = InputNonClientPointerSource.GetForWindowId(_appWindow.Id);
        inputSource.SetRegionRects(NonClientRegionKind.Caption,
            [new RectInt32(0, 0, OverlayWidth, DragHandleHeight)]);

        // Dark acrylic backdrop — replaces WS_EX_LAYERED P/Invoke entirely.
        // Window.SystemBackdrop is the native WinUI 3 / Windows App SDK API (no P/Invoke).
        // Force dark theme so the overlay is always dark regardless of system setting.
        SystemBackdrop = new DesktopAcrylicBackdrop();
        if (Content is FrameworkElement root)
            root.RequestedTheme = ElementTheme.Dark;
    }

    public void ShowSegment(SubtitleSegment segment)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            SubtitleText.Text = segment.Text;
            _segmentCount++;
            RecordingIndicator.Text = $"● {_segmentCount} segment{(_segmentCount == 1 ? "" : "s")}";
        });
    }

    public void SetListening()
    {
        DispatcherQueue.TryEnqueue(() => RecordingIndicator.Text = "● Listening…");
    }

    private void StopBtn_Click(object sender, RoutedEventArgs e)
    {
        StopRequested?.Invoke(this, EventArgs.Empty);
    }
}
