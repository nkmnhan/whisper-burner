using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using System;
using System.Collections.ObjectModel;
using WhisperLive.Helpers;
using Windows.Graphics;

namespace WhisperLive.Overlay;

public sealed partial class CaptionOverlayWindow : Window
{
    private const int MaxLines = 3;
    private const int WindowWidth = 820;
    private const int WindowHeight = 130;

    private double _dragStartX;
    private double _dragStartY;

    public ObservableCollection<CaptionLine> Lines { get; } = [];

    public CaptionOverlayWindow()
    {
        InitializeComponent();
        WindowHelper.TrackWindow(this);
        ConfigureWindow();
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
        AppWindow.TitleBar.ExtendsContentIntoTitleBar = true;

        // Position bottom-centre of primary display
        var area = DisplayArea.Primary.WorkArea;
        int x = (area.Width - WindowWidth) / 2;
        int y = area.Height - WindowHeight - 48;
        AppWindow.MoveAndResize(new RectInt32(x, y, WindowWidth, WindowHeight));
    }

    public void ShowSegment(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;

        DispatcherQueue.TryEnqueue(() =>
        {
            if (Lines.Count >= MaxLines)
                Lines.RemoveAt(0);
            Lines.Add(new CaptionLine { Text = text });
        });
    }

    public void ClearLines() =>
        DispatcherQueue.TryEnqueue(Lines.Clear);

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
