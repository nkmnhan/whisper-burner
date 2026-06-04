using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using System.Collections.Generic;

namespace WhisperLive.Helpers;

public static class WindowHelper
{
    public static void TrackWindow(Window window)
    {
        window.Closed += (_, _) => _activeWindows.Remove(window);
        _activeWindows.Add(window);
    }

    public static void SetWindowMinSize(Window window, double width, double height)
    {
        if (window.Content is not FrameworkElement windowContent) return;
        if (windowContent.XamlRoot is null) return;
        if (window.AppWindow.Presenter is not OverlappedPresenter presenter) return;

        var scale = windowContent.XamlRoot.RasterizationScale;
        presenter.PreferredMinimumWidth = (int)(width * scale);
        presenter.PreferredMinimumHeight = (int)(height * scale);
    }

    public static List<Window> ActiveWindows => _activeWindows;

    private static readonly List<Window> _activeWindows = [];
}
