using Microsoft.UI;
using Microsoft.UI.Xaml;
using Windows.UI;

namespace WhisperLive.Helpers;

internal static class TitleBarHelper
{
    // Workaround: AppWindow TitleBar doesn't update caption button colors correctly
    // when changed while the app is running. https://task.ms/44172495
    public static void ApplySystemThemeToCaptionButtons(Window window, ElementTheme currentTheme)
    {
        if (window.AppWindow is null) return;

        var foreground = currentTheme == ElementTheme.Dark ? Colors.White : Colors.Black;
        window.AppWindow.TitleBar.ButtonForegroundColor = foreground;
        window.AppWindow.TitleBar.ButtonHoverForegroundColor = foreground;

        var hoverBg = currentTheme == ElementTheme.Dark
            ? Color.FromArgb(24, 255, 255, 255)
            : Color.FromArgb(24, 0, 0, 0);
        window.AppWindow.TitleBar.ButtonHoverBackgroundColor = hoverBg;
    }
}
