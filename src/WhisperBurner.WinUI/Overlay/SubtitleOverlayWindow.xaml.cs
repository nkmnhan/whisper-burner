using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WhisperBurner.WinUI.Models;
using Windows.Graphics;

namespace WhisperBurner.WinUI.Overlay;

public sealed partial class SubtitleOverlayWindow : Window
{
    private const int    OverlayWidth    = 780;
    private const int    OverlayHeight   = 200;
    private const int    HeaderHeight    = 36;
    private const int    MaxVisibleLines = 10;
    private const double FontSizeDefault = 28;
    private const double FontSizeMin     = 14;
    private const double FontSizeMax     = 60;

    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h, int n);
    [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr h, int n, int v);
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int a, ref int p, int s);
    const int GWL_EXSTYLE               = -20;
    const int WS_EX_LAYERED             = 0x00080000;
    const int WS_EX_NOREDIRECTIONBITMAP = 0x00200000;
    const int DWMWA_NCRENDERING_POLICY  = 2;   // disable shadow
    const int DWMNCRP_DISABLED          = 1;
    const int DWMWA_WINDOW_CORNER_PREF  = 33;
    const int DWMWA_BORDER_COLOR        = 34;
    const int DWMWCP_ROUND              = 2;
    const int DWMWA_COLOR_NONE          = unchecked((int)0xFFFFFFFE);

    public event EventHandler? StopRequested;

    private readonly AppWindow _appWindow;
    private readonly ObservableCollection<SubtitleLine> _visible = new();
    private double _fontSize = FontSizeDefault;
    private int    _segmentCount;
    private int    _anchorX, _anchorY;

    public SubtitleOverlayWindow(CaptureRegion region)
    {
        InitializeComponent();
        LinesRepeater.ItemsSource = _visible;

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        _appWindow = AppWindow.GetFromWindowId(Win32Interop.GetWindowIdFromWindow(hwnd));

        // Per-pixel transparency (no DwmExtendFrameIntoClientArea — that creates the white glow)
        try
        {
            var ex = GetWindowLong(hwnd, GWL_EXSTYLE);
            SetWindowLong(hwnd, GWL_EXSTYLE, ex | WS_EX_LAYERED | WS_EX_NOREDIRECTIONBITMAP);
        }
        catch { }

        var presenter = (OverlappedPresenter)_appWindow.Presenter;
        presenter.IsAlwaysOnTop = true;
        presenter.IsResizable   = true;
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        try { presenter.SetBorderAndTitleBar(false, false); } catch { }

        // Rounded corners, no system border, no DWM shadow
        try
        {
            int pref   = DWMWCP_ROUND;    DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREF, ref pref, 4);
            int nb     = DWMWA_COLOR_NONE; DwmSetWindowAttribute(hwnd, DWMWA_BORDER_COLOR, ref nb, 4);
            int noShadow = DWMNCRP_DISABLED; DwmSetWindowAttribute(hwnd, DWMWA_NCRENDERING_POLICY, ref noShadow, 4);
        }
        catch { }

        // Position at bottom-centre of capture region
        _anchorX = Math.Max(region.X, region.X + (region.Width - OverlayWidth)  / 2);
        _anchorY = Math.Max(region.Y, region.Y +  region.Height - OverlayHeight - 24);
        _appWindow.MoveAndResize(new RectInt32(_anchorX, _anchorY, OverlayWidth, OverlayHeight));

        // Caption drag zone — full top strip
        try
        {
            var src = InputNonClientPointerSource.GetForWindowId(_appWindow.Id);
            src.SetRegionRects(NonClientRegionKind.Caption,
                [new RectInt32(0, 0, OverlayWidth, HeaderHeight)]);
        }
        catch { }

        _appWindow.Changed += (_, args) =>
        {
            if (args.DidPositionChange)
            {
                _anchorX = _appWindow.Position.X;
                _anchorY = _appWindow.Position.Y;
            }
        };

        if (Content is FrameworkElement root)
        {
            root.RequestedTheme = ElementTheme.Dark;
            // Show controls on mouse enter, hide on exit
            root.PointerEntered += (_, _) => TopControls.Opacity = 1;
            root.PointerExited  += (_, _) => TopControls.Opacity = 0;
        }
    }

    public void ShowSegment(SubtitleSegment segment)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            _segmentCount++;
            RecordingIndicator.Text = $"Live Caption · {_segmentCount}";
            _visible.Add(new SubtitleLine { Text = segment.Text, FontSize = _fontSize });
            while (_visible.Count > MaxVisibleLines) _visible.RemoveAt(0);
            Scroller.ChangeView(null, double.MaxValue, null, disableAnimation: true);
        });
    }

    public void SetListening() =>
        DispatcherQueue.TryEnqueue(() =>
        {
            RecordingIndicator.Text = "Live Caption";
            RecordingDot.Fill = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 68, 68));
        });

    public void ClearSegments() => DispatcherQueue.TryEnqueue(() => _visible.Clear());

    private void FontSmallBtn_Click(object sender, RoutedEventArgs e) => AdjustFontSize(-4);
    private void FontLargeBtn_Click(object sender, RoutedEventArgs e) => AdjustFontSize(+4);

    private void AdjustFontSize(double delta)
    {
        _fontSize = Math.Clamp(_fontSize + delta, FontSizeMin, FontSizeMax);
        foreach (var line in _visible) line.FontSize = _fontSize;
    }

    private void StopBtn_Click(object sender, RoutedEventArgs e) =>
        StopRequested?.Invoke(this, EventArgs.Empty);
}
