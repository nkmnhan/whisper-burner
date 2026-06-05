using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using System;
using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using WhisperLive.Helpers;
using Windows.Graphics;
using Windows.UI;
using WinRT;

namespace WhisperLive.Overlay;

public sealed partial class CaptionOverlayWindow : Window
{
    private const int MaxBuffer = 8;
    private const int DisplayCollapsed = 2;
    private const int DisplayExpanded = 8;

    private const int WindowWidth = 820;
    private const int WindowHeightCollapsed = 118;  // 2 lines × ~27px + label + chevron row
    private const int WindowHeightExpanded = 280;   // 8 lines × ~27px + label + chevron row

    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWCP_ROUND = 2;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    private double _dragStartX;
    private double _dragStartY;
    private bool _isExpanded;
    private DesktopAcrylicController? _acrylicController;
    private SystemBackdropConfiguration? _backdropConfig;
    private readonly List<CaptionLine> _lineBuffer = [];

    public ObservableCollection<CaptionLine> DisplayLines { get; } = [];

    public CaptionOverlayWindow()
    {
        InitializeComponent();
        WindowHelper.TrackWindow(this);
        ConfigureWindow();
        ApplyAcrylicBackdrop();
        RootGrid.ManipulationMode = ManipulationModes.TranslateX | ManipulationModes.TranslateY;
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

    // Acrylic backdrop only affects the background — XAML content (text) stays fully opaque.
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
            TintColor = Color.FromArgb(255, 43, 43, 43), // #2B2B2B — Chrome Live Caption dark
            TintOpacity = 0.75f,   // 0.6–0.8 is the recommended range for dark acrylic
            LuminosityOpacity = 0.15f  // low value preserves darkness and depth
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

    public void ShowSegment(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;

        DispatcherQueue.TryEnqueue(() =>
        {
            _lineBuffer.Add(new CaptionLine { Text = text });
            if (_lineBuffer.Count > MaxBuffer)
                _lineBuffer.RemoveAt(0);
            RefreshDisplayLines();
        });
    }

    public void ClearLines() =>
        DispatcherQueue.TryEnqueue(() =>
        {
            _lineBuffer.Clear();
            DisplayLines.Clear();
        });

    public void SetLanguage(string language) =>
        DispatcherQueue.TryEnqueue(() => LanguageLabel.Text = language);

    private void RefreshDisplayLines()
    {
        int take = _isExpanded ? DisplayExpanded : DisplayCollapsed;
        int start = Math.Max(0, _lineBuffer.Count - take);
        DisplayLines.Clear();
        for (int i = start; i < _lineBuffer.Count; i++)
            DisplayLines.Add(_lineBuffer[i]);
    }

    private void OnExpandClicked(object sender, RoutedEventArgs e)
    {
        _isExpanded = !_isExpanded;

        // Update icons: E70E = chevron up (expand), E70D = chevron down (collapse)
        ChevronIcon.Glyph = _isExpanded ? "\uE70D" : "\uE70E";

        // Grow/shrink the window upward, keeping the bottom edge fixed
        int newHeight = _isExpanded ? WindowHeightExpanded : WindowHeightCollapsed;
        var area = DisplayArea.Primary.WorkArea;
        int bottomEdge = AppWindow.Position.Y + AppWindow.Size.Height;
        int newY = bottomEdge - newHeight;
        AppWindow.MoveAndResize(new RectInt32(AppWindow.Position.X, newY, WindowWidth, newHeight));

        RefreshDisplayLines();
    }

    private void OnCloseClicked(object sender, RoutedEventArgs e) => Close();

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
