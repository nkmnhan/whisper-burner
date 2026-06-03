using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using WhisperBurner.WinUI.Models;
using Windows.Foundation;

namespace WhisperBurner.WinUI.Overlay;

public sealed partial class RegionSelectorWindow : Window
{
    public event EventHandler<CaptureRegion>? RegionSelected;
    public event EventHandler? SelectionCancelled;

    private Point _start;
    private bool _dragging;
    private readonly AppWindow _appWindow;

    public RegionSelectorWindow(BitmapImage screenshot)
    {
        InitializeComponent();

        // Image is already decoded before this constructor is called — no flash
        ScreenshotBg.Source = screenshot;

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        _appWindow = AppWindow.GetFromWindowId(Win32Interop.GetWindowIdFromWindow(hwnd));

        var presenter = (OverlappedPresenter)_appWindow.Presenter;
        presenter.IsAlwaysOnTop = true;
        presenter.SetBorderAndTitleBar(false, false);
        presenter.IsResizable = false;

        var display = DisplayArea.GetFromWindowId(_appWindow.Id, DisplayAreaFallback.Primary);
        _appWindow.MoveAndResize(display.OuterBounds);

        Activated += (_, _) => RootGrid.Focus(FocusState.Programmatic);
    }

    private void RootGrid_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Escape)
        {
            SelectionCancelled?.Invoke(this, EventArgs.Empty);
            Close();
        }
    }

    private void RootGrid_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        _start = e.GetCurrentPoint(OverlayCanvas).Position;
        _dragging = true;
        SelectionRect.Visibility = Visibility.Visible;
        SelectionRect.Width = 0;
        SelectionRect.Height = 0;
        Microsoft.UI.Xaml.Controls.Canvas.SetLeft(SelectionRect, _start.X);
        Microsoft.UI.Xaml.Controls.Canvas.SetTop(SelectionRect, _start.Y);
        HLine.Visibility = Visibility.Collapsed;
        VLine.Visibility = Visibility.Collapsed;
        RootGrid.CapturePointer(e.Pointer);
    }

    private void RootGrid_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        var pos = e.GetCurrentPoint(OverlayCanvas).Position;

        if (!_dragging)
        {
            Microsoft.UI.Xaml.Controls.Canvas.SetTop(HLine, pos.Y);
            HLine.X2 = RootGrid.ActualWidth;
            Microsoft.UI.Xaml.Controls.Canvas.SetLeft(VLine, pos.X);
            VLine.Y2 = RootGrid.ActualHeight;
            CoordLabel.Text = $"({(int)pos.X}, {(int)pos.Y})";
            return;
        }

        var x = Math.Min(pos.X, _start.X);
        var y = Math.Min(pos.Y, _start.Y);
        var w = Math.Abs(pos.X - _start.X);
        var h = Math.Abs(pos.Y - _start.Y);
        Microsoft.UI.Xaml.Controls.Canvas.SetLeft(SelectionRect, x);
        Microsoft.UI.Xaml.Controls.Canvas.SetTop(SelectionRect, y);
        SelectionRect.Width = w;
        SelectionRect.Height = h;
        CoordLabel.Text = $"{(int)w} × {(int)h}  at ({(int)x}, {(int)y})";
    }

    private void RootGrid_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_dragging) return;
        _dragging = false;
        RootGrid.ReleasePointerCapture(e.Pointer);

        var region = new CaptureRegion(
            (int)Microsoft.UI.Xaml.Controls.Canvas.GetLeft(SelectionRect),
            (int)Microsoft.UI.Xaml.Controls.Canvas.GetTop(SelectionRect),
            (int)SelectionRect.Width,
            (int)SelectionRect.Height);

        if (region.IsValid)
        {
            RegionSelected?.Invoke(this, region);
            Close();
        }
        else
        {
            SelectionRect.Visibility = Visibility.Collapsed;
            HLine.Visibility = Visibility.Visible;
            VLine.Visibility = Visibility.Visible;
            _dragging = false;
        }
    }
}
