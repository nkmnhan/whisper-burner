using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using System.Runtime.InteropServices;
using WhisperLive.Helpers;
using WhisperLive.Models;
using Windows.Graphics;
using Windows.UI;
using WinRT;

namespace WhisperLive.Overlay;

public sealed partial class CaptionOverlayWindow : Window
{
    private const int WindowWidth = 860;
    private const int WindowHeightCollapsed = 220;
    private const int WindowHeightExpanded  = 440;

    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWCP_ROUND = 2;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    private double _dragStartX;
    private double _dragStartY;
    private bool _isExpanded;
    private DesktopAcrylicController? _acrylicController;
    private SystemBackdropConfiguration? _backdropConfig;

    public CaptionOverlayWindow()
    {
        InitializeComponent();
        // Intentionally NOT tracked via WindowHelper — the overlay manages its own
        // dark theme independently and must not affect the main window's theme.
        ConfigureWindow();
        // Force dark theme on this window's content only, isolated from ThemeHelper
        ((FrameworkElement)Content).RequestedTheme = ElementTheme.Dark;
        ApplyAcrylicBackdrop();
        RootGrid.ManipulationMode = ManipulationModes.TranslateX | ManipulationModes.TranslateY;
        CaptionText.SizeChanged += (_, _) => ScrollToBottom();
    }

    private void ConfigureWindow()
    {
        // Borderless, always-on-top
        var presenter = OverlappedPresenter.CreateForToolWindow();
        presenter.IsResizable = false;
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        presenter.IsAlwaysOnTop = true;
        AppWindow.SetPresenter(presenter);

        AppWindow.IsShownInSwitchers = false;

        // Collapse the title bar entirely — removes all OS chrome buttons (close/min/max).
        // Our XAML draws its own buttons; the OS close button's red hover can't be suppressed any other way.
        AppWindow.TitleBar.ExtendsContentIntoTitleBar = true;
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Collapsed;

        // Rounded corners
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        int roundCorners = DWMWCP_ROUND;
        DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref roundCorners, sizeof(int));

        // Position bottom-centre of primary display (collapsed height)
        var area = DisplayArea.Primary.WorkArea;
        int x = (area.Width - WindowWidth) / 2;
        int y = area.Height - WindowHeightCollapsed - 48;
        AppWindow.MoveAndResize(new RectInt32(x, y, WindowWidth, WindowHeightCollapsed));
    }


    // Acrylic provides the frosted blur; the dark Rectangle overlay adds reliable dark tint.
    private void ApplyAcrylicBackdrop()
    {
        if (!DesktopAcrylicController.IsSupported()) return;

        _backdropConfig = new SystemBackdropConfiguration
        {
            IsInputActive = true,
            Theme = SystemBackdropTheme.Dark
        };

        _acrylicController = new DesktopAcrylicController
        {
            Kind = DesktopAcrylicKind.Base,
            TintColor = Color.FromArgb(255, 10, 10, 10),
            TintOpacity = 0.5f,
            LuminosityOpacity = 0.3f
        };

        _acrylicController.AddSystemBackdropTarget(this.As<ICompositionSupportsSystemBackdrop>());
        _acrylicController.SetSystemBackdropConfiguration(_backdropConfig);

        Closed += (_, _) =>
        {
            _acrylicController?.Dispose();
            _acrylicController = null;
            _backdropConfig = null;
        };
    }


    public void ShowSegment(SubtitleSegment seg)
    {
        DispatcherQueue.TryEnqueue(() =>
            CaptionText.Text += (CaptionText.Text.Length > 0 ? " " : "") + seg.Text);
        // SizeChanged hook scrolls to bottom after layout
    }

    public void ClearLines() =>
        DispatcherQueue.TryEnqueue(() => CaptionText.Text = string.Empty);

    public void SetLanguage(string language) =>
        DispatcherQueue.TryEnqueue(() => LanguageLabel.Text = language);

    private void ScrollToBottom() =>
        CaptionScroller.ChangeView(null, double.MaxValue, null, disableAnimation: true);

    private void OnExpandClicked(object sender, RoutedEventArgs e)
    {
        _isExpanded = !_isExpanded;
        ChevronIcon.Glyph = _isExpanded ? "\uE70D" : "\uE70E";

        int newHeight = _isExpanded ? WindowHeightExpanded : WindowHeightCollapsed;
        int bottomEdge = AppWindow.Position.Y + AppWindow.Size.Height;
        AppWindow.MoveAndResize(new RectInt32(AppWindow.Position.X, bottomEdge - newHeight, WindowWidth, newHeight));

        ScrollToBottom();
    }

    private void OnCloseClicked(object sender, RoutedEventArgs e) => AppWindow.Hide();

    public void UpdatePauseState(bool isPaused)
    {
        DispatcherQueue.TryEnqueue(() =>
            OverlayPauseIcon.Glyph = isPaused ? "\uE768" : "\uE769"); // Play : Pause
    }

    private void OnOverlayPauseClicked(object sender, RoutedEventArgs e)
    {
        var svc = ((App)Application.Current).RecordingService;
        if (svc.IsPaused)
            _ = svc.ResumeAsync();
        else
            _ = svc.PauseAsync();
        UpdatePauseState(svc.IsPaused);
    }

    private void OnPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        TopBar.Visibility = Visibility.Visible;
        LiveTranscriptLabel.Visibility = Visibility.Collapsed;
    }

    private void OnPointerExited(object sender, PointerRoutedEventArgs e)
    {
        TopBar.Visibility = Visibility.Collapsed;
        LiveTranscriptLabel.Visibility = Visibility.Visible;
    }

    // ── Drag to reposition ────────────────────────────────────────────────────

    private void OnManipulationStarted(object sender, ManipulationStartedRoutedEventArgs e)
    {
        var pos = AppWindow.Position;
        _dragStartX = pos.X;
        _dragStartY = pos.Y;
    }

    private void OnManipulationDelta(object sender, ManipulationDeltaRoutedEventArgs e)
    {
        var scale = RootGrid.XamlRoot?.RasterizationScale ?? 1.0;
        int newX = (int)(_dragStartX + e.Cumulative.Translation.X * scale);
        int newY = (int)(_dragStartY + e.Cumulative.Translation.Y * scale);
        AppWindow.Move(new PointInt32(newX, newY));
    }
}
