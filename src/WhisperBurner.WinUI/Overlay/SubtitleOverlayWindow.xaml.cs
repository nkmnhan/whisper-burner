using System.Collections.ObjectModel;
using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using WhisperBurner.WinUI.Models;
using Windows.Graphics;

namespace WhisperBurner.WinUI.Overlay;

public sealed partial class SubtitleOverlayWindow : Window
{
    private const int OverlayWidth = 720;
    private const int OverlayHeight = 380;
    private const int DragHandleHeight = 22;
    private const int MaxVisibleLines = 10;

    public event EventHandler? StopRequested;

    private readonly AppWindow _appWindow;
    private readonly ObservableCollection<SubtitleSegment> _visible = new();
    private bool _frozen;
    private int _segmentCount;

    public SubtitleOverlayWindow(CaptureRegion region)
    {
        InitializeComponent();
        LinesRepeater.ItemsSource = _visible;

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        _appWindow = AppWindow.GetFromWindowId(Win32Interop.GetWindowIdFromWindow(hwnd));

        var presenter = (OverlappedPresenter)_appWindow.Presenter;
        presenter.IsAlwaysOnTop = true;
        presenter.IsResizable = true;
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        presenter.SetBorderAndTitleBar(false, false);

        int x = region.X + (region.Width - OverlayWidth) / 2;
        int y = region.Y + region.Height - OverlayHeight - 20;
        _appWindow.MoveAndResize(new RectInt32(Math.Max(0, x), Math.Max(0, y), OverlayWidth, OverlayHeight));

        var inputSource = InputNonClientPointerSource.GetForWindowId(_appWindow.Id);
        inputSource.SetRegionRects(NonClientRegionKind.Caption,
            [new RectInt32(0, 0, OverlayWidth, DragHandleHeight)]);

        SystemBackdrop = new DesktopAcrylicBackdrop();
        if (Content is FrameworkElement root)
            root.RequestedTheme = ElementTheme.Dark;
    }

    public void ShowSegment(SubtitleSegment segment)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            _segmentCount++;
            RecordingIndicator.Text = $"● {_segmentCount} segment{(_segmentCount == 1 ? "" : "s")}";
            _visible.Add(segment);
            while (_visible.Count > MaxVisibleLines)
                _visible.RemoveAt(0);
            if (!_frozen)
                Scroller.ChangeView(null, double.MaxValue, null, disableAnimation: true);
        });
    }

    public void SetListening()
    {
        DispatcherQueue.TryEnqueue(() => RecordingIndicator.Text = "● Listening…");
    }

    private void FreezeBtn_Click(object sender, RoutedEventArgs e)
    {
        _frozen = !_frozen;
        FreezeBtn.Content = _frozen ? "▶ Resume" : "❚❚ Pause";
        if (!_frozen)
            Scroller.ChangeView(null, double.MaxValue, null, disableAnimation: true);
    }

    private void StopBtn_Click(object sender, RoutedEventArgs e)
    {
        StopRequested?.Invoke(this, EventArgs.Empty);
    }
}
