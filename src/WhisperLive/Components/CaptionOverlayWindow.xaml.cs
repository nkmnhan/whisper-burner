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
using Windows.Graphics;
using Windows.UI;
using WinRT;

namespace WhisperLive.Components;

public sealed partial class CaptionOverlayWindow : Window
{
    private const int WindowWidth = 860;
    private const int WindowHeightCollapsed = 280;
    private const int WindowHeightExpanded  = 440;
    private const int MaxCollapsedRows = 10;
    private const int MaxExpandedRows  = 30;

    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWCP_ROUND = 2;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

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

        var allSegments = ((App)Application.Current).TranscriptViewModel.Segments;
        allSegments.CollectionChanged += OnSegmentsChanged;

        Closed += (_, _) =>
        {
            allSegments.CollectionChanged -= OnSegmentsChanged;
            _acrylicController?.Dispose();
            _acrylicController = null;
            _backdropConfig = null;
        };
    }

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

        var area = DisplayArea.Primary.WorkArea;
        int x = (area.Width - WindowWidth) / 2;
        int y = area.Height - WindowHeightCollapsed - 48;
        AppWindow.MoveAndResize(new RectInt32(x, y, WindowWidth, WindowHeightCollapsed));
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

    public void SetLanguage(string language) =>
        DispatcherQueue.TryEnqueue(() => LanguageLabel.Text = language);

    private void OnExpandClicked(object sender, RoutedEventArgs e)
    {
        _isExpanded = !_isExpanded;
        // Expanded: E70E (ChevronUp ^) = prompt to collapse; Collapsed: E70D (ChevronDown ∨) = prompt to expand
        ChevronIcon.Glyph = _isExpanded ? "" : ""; // ^ collapse : ∨ expand

        int newHeight = _isExpanded ? WindowHeightExpanded : WindowHeightCollapsed;
        int bottomEdge = AppWindow.Position.Y + AppWindow.Size.Height;
        AppWindow.MoveAndResize(new RectInt32(AppWindow.Position.X, bottomEdge - newHeight, WindowWidth, newHeight));

        ResyncRows();
    }

    private void OnCloseClicked(object sender, RoutedEventArgs e)
    {
        AppWindow.Hide();
        Hidden?.Invoke(this, EventArgs.Empty);
    }

    public void UpdatePauseState(bool isPaused) =>
        DispatcherQueue.TryEnqueue(() =>
            OverlayPauseIcon.Glyph = isPaused ? "" : ""); // Play (resume) : Pause

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
