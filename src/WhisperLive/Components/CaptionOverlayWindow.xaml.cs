using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Runtime.InteropServices;
using WhisperLive.Models;
using WhisperLive.Services.Audio;
using Windows.Graphics;
using Windows.UI;
using WinRT;

namespace WhisperLive.Components;

public sealed partial class CaptionOverlayWindow : Window
{
    private const int WindowWidth = 860;
    // MAX window heights in DIPs. ResizeToContent shrinks the window to fit content,
    // so there is never dead space above the captions.
    private const int WindowHeightCollapsed = 200;
    private const int WindowHeightExpanded  = 360;
    // Vertical chrome in DIPs: top-margin(12) + top-padding(8) + header-row(28) + list-margin(4) + bottom-margin(12).
    private const int ChromeHeightDip = 64;
    private const int MaxCollapsedRows = 8;
    private const int MaxExpandedRows  = 25;

    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWCP_ROUND = 2;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    private readonly ObservableCollection<TranslatedSegmentView> _overlayRows = [];
    private double _dragStartX;
    private double _dragStartY;
    private bool _isExpanded;
    private DesktopAcrylicController? _acrylicController;
    private SystemBackdropConfiguration? _backdropConfig;

    /// <summary>Raised when the user dismisses the overlay via the close button.</summary>
    public event EventHandler? Hidden;

    public CaptionOverlayWindow()
    {
        InitializeComponent();
        ConfigureWindow();
        ((FrameworkElement)Content).RequestedTheme = ElementTheme.Dark;
        ApplyAcrylicBackdrop();

        CaptionsPanel.ItemsSource = _overlayRows;
        CaptionsPanel.MaxHeight = WindowHeightCollapsed - ChromeHeightDip;

        // Window follows its content height (bottom-anchored), so few lines
        // never leave a gap and many lines grow the box up to the cap.
        ContentRoot.SizeChanged += (_, _) => ResizeToContent();

        var allSegments = ((App)Application.Current).TranscriptViewModel.Segments;
        allSegments.CollectionChanged += OnSegmentsChanged;

        var manager = ((App)Application.Current).RecordingManager;
        manager.StateChanged += OnManagerStateChanged;

        Closed += (_, _) =>
        {
            allSegments.CollectionChanged -= OnSegmentsChanged;
            manager.StateChanged -= OnManagerStateChanged;
            _acrylicController?.Dispose();
            _acrylicController = null;
            _backdropConfig = null;
        };
    }

    private void OnManagerStateChanged(object? sender, RecordingState state) =>
        DispatcherQueue.TryEnqueue(() =>
            OverlayPauseIcon.Glyph = state == RecordingState.Paused ? "" : "");

    private void OnSegmentsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        var max = _isExpanded ? MaxExpandedRows : MaxCollapsedRows;

        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            _overlayRows.Clear();
            return;
        }

        if (e.Action == NotifyCollectionChangedAction.Add && e.NewItems is { } added)
        {
            foreach (TranslatedSegmentView item in added)
                _overlayRows.Add(item);
            while (_overlayRows.Count > max)
                _overlayRows.RemoveAt(0);
        }
    }

    private void ResyncRows()
    {
        var all = ((App)Application.Current).TranscriptViewModel.Segments;
        var max = _isExpanded ? MaxExpandedRows : MaxCollapsedRows;
        var slice = all.TakeLast(max).ToList();
        _overlayRows.Clear();
        foreach (var item in slice)
            _overlayRows.Add(item);
    }

    private void ConfigureWindow()
    {
        var presenter = OverlappedPresenter.CreateForToolWindow();
        presenter.IsResizable = false;
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        presenter.IsAlwaysOnTop = true;
        AppWindow.SetPresenter(presenter);

        AppWindow.IsShownInSwitchers = false;

        AppWindow.TitleBar.ExtendsContentIntoTitleBar = true;
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Collapsed;

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        int roundCorners = DWMWCP_ROUND;
        DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref roundCorners, sizeof(int));

        // Scale DIP constants to physical pixels for the actual display DPI.
        // The previous code passed DIP values directly as physical pixels, which caused
        // the window to be the wrong size at non-100% DPI (e.g. too narrow at 125%).
        uint dpi = GetDpiForWindow(hwnd);
        double scale = dpi / 96.0;
        int physWidth = (int)Math.Round(WindowWidth * scale);
        int physHeightCollapsed = (int)Math.Round(WindowHeightCollapsed * scale);

        var area = DisplayArea.Primary.WorkArea;
        int x = (area.Width - physWidth) / 2;
        int y = area.Height - physHeightCollapsed - 48;
        AppWindow.MoveAndResize(new RectInt32(x, y, physWidth, physHeightCollapsed));
    }

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
    }

    public void SetLanguage(string language, string? translationTarget = null) =>
        DispatcherQueue.TryEnqueue(() =>
        {
            LanguageLabel.Text = language;
            var hasTarget = translationTarget is not null;
            LangArrow.Visibility = hasTarget ? Visibility.Visible : Visibility.Collapsed;
            TranslationTargetChip.Visibility = hasTarget ? Visibility.Visible : Visibility.Collapsed;
            if (hasTarget) TranslationTargetLabel.Text = translationTarget!;
        });

    private void OnExpandClicked(object sender, RoutedEventArgs e)
    {
        _isExpanded = !_isExpanded;
        var glyph = _isExpanded ? "\uE70E" : "\uE70D";
        ChevronIcon.Glyph = glyph;
        TopBarChevronIcon.Glyph = glyph;

        // Raise/lower the caption cap; ResyncRows + ResizeToContent then size the
        // window to whatever content actually fits.
        CaptionsPanel.MaxHeight = (_isExpanded ? WindowHeightExpanded : WindowHeightCollapsed) - ChromeHeightDip;
        ResyncRows();
        ResizeToContent();
    }

    /// <summary>
    /// Resizes the window to fit its content height (clamped to the current cap),
    /// keeping the bottom edge fixed so the overlay hugs its anchor with no gap.
    /// The top edge is clamped to the work area so the window never grows off-screen.
    /// </summary>
    private void ResizeToContent()
    {
        if (ContentRoot.XamlRoot is null || ContentRoot.ActualHeight <= 0) return;

        var scale = ContentRoot.XamlRoot.RasterizationScale;
        // ActualHeight includes ContentRoot's Padding but excludes its Margin (top=12, bottom=12).
        double dipHeight = ContentRoot.ActualHeight + 24;
        int maxHeight = _isExpanded ? WindowHeightExpanded : WindowHeightCollapsed;
        dipHeight = Math.Min(dipHeight, maxHeight);

        int physHeight = (int)Math.Ceiling(dipHeight * scale);
        if (physHeight == AppWindow.Size.Height) return;

        int bottomEdge = AppWindow.Position.Y + AppWindow.Size.Height;
        int newTop = bottomEdge - physHeight;

        // Clamp top to work area so the window never grows above the screen boundary.
        var workArea = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        if (newTop < workArea.Y)
        {
            newTop = workArea.Y;
            physHeight = bottomEdge - newTop;
        }

        AppWindow.MoveAndResize(new RectInt32(
            AppWindow.Position.X, newTop, AppWindow.Size.Width, physHeight));
    }

    private void OnCloseClicked(object sender, RoutedEventArgs e)
    {
        AppWindow.Hide();
        Hidden?.Invoke(this, EventArgs.Empty);
    }

    private void OnOverlayPauseClicked(object sender, RoutedEventArgs e)
    {
        var manager = ((App)Application.Current).RecordingManager;
        if (manager.State == RecordingState.Paused)
            _ = manager.ResumeAsync();
        else if (manager.State == RecordingState.Recording)
            _ = manager.PauseAsync();
    }

    private void OnPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        TopBar.Visibility = Visibility.Visible;
        HeaderBar.Visibility = Visibility.Collapsed;
    }

    private void OnPointerExited(object sender, PointerRoutedEventArgs e)
    {
        TopBar.Visibility = Visibility.Collapsed;
        HeaderBar.Visibility = Visibility.Visible;
    }

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

        // Clamp to work area so the window can't be dragged off-screen.
        var workArea = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        newX = Math.Clamp(newX, workArea.X, workArea.X + workArea.Width - AppWindow.Size.Width);
        newY = Math.Clamp(newY, workArea.Y, workArea.Y + workArea.Height - AppWindow.Size.Height);

        AppWindow.Move(new PointInt32(newX, newY));
    }
}
