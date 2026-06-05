using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using WhisperLive.Helpers;
using Windows.Graphics;
using Windows.UI;
using WinRT;

namespace WhisperLive.Overlay;

public sealed partial class CaptionOverlayWindow : Window
{
    private const int MaxBuffer = 50;   // segments kept in history for scroll-back

    private const int WindowWidth = 860;
    private const int WindowHeightCollapsed = 220;  // ~4 visible lines + header + chevron
    private const int WindowHeightExpanded  = 440;  // ~12 visible lines + header + chevron

    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWCP_ROUND = 2;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    private double _dragStartX;
    private double _dragStartY;
    private bool _isExpanded;
    private DesktopAcrylicController? _acrylicController;
    private SystemBackdropConfiguration? _backdropConfig;
    private readonly List<string> _lineBuffer = [];

    public ObservableCollection<CaptionLine> DisplayLines { get; } = [];

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


    public void ShowSegment(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;

        DispatcherQueue.TryEnqueue(() =>
        {
            foreach (var line in SplitIntoLines(text))
            {
                DisplayLines.Add(new CaptionLine { Text = line });
                _lineBuffer.Add(line);
                if (_lineBuffer.Count > MaxBuffer)
                {
                    _lineBuffer.RemoveAt(0);
                    DisplayLines.RemoveAt(0);
                }
            }
            ScrollToBottom();
        });
    }

    // Splits a segment into individual display lines so that "latest N lines"
    // means N visual lines, not N potentially-long segments.
    private static IEnumerable<string> SplitIntoLines(string text)
    {
        var parts = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts)
        {
            var trimmed = part.Trim();
            if (string.IsNullOrEmpty(trimmed)) continue;

            if (trimmed.Length <= 90)
            {
                yield return trimmed;
                continue;
            }

            // Split at sentence endings for longer text
            int start = 0;
            for (int i = 0; i < trimmed.Length - 1; i++)
            {
                char c = trimmed[i];
                if ((c == '.' || c == '?' || c == '!') && trimmed[i + 1] == ' ' && i - start >= 20)
                {
                    yield return trimmed[start..(i + 1)].Trim();
                    start = i + 2;
                }
            }
            if (start < trimmed.Length)
                yield return trimmed[start..].Trim();
        }
    }

    public void ClearLines() =>
        DispatcherQueue.TryEnqueue(() =>
        {
            _lineBuffer.Clear();
            DisplayLines.Clear();
        });

    public void SetLanguage(string language) =>
        DispatcherQueue.TryEnqueue(() => LanguageLabel.Text = language);

    // Scroll to the bottom after layout has settled so the latest line is visible.
    private void ScrollToBottom()
    {
        CaptionScroller.UpdateLayout();
        CaptionScroller.ChangeView(null, CaptionScroller.ScrollableHeight, null, disableAnimation: true);
    }

    private void RefreshDisplayLines()
    {
        DisplayLines.Clear();
        foreach (var line in _lineBuffer)
            DisplayLines.Add(new CaptionLine { Text = line });
        ScrollToBottom();
    }

    private void OnExpandClicked(object sender, RoutedEventArgs e)
    {
        _isExpanded = !_isExpanded;

        // E70E = chevron up (expand), E70D = chevron down (collapse)
        ChevronIcon.Glyph = _isExpanded ? "\uE70D" : "\uE70E";

        // Grow/shrink upward keeping the bottom edge fixed
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
