using Microsoft.UI.Xaml;

namespace WhisperLive.Helpers;

public static class ThemeHelper
{
    public static ElementTheme ActualTheme
    {
        get
        {
            foreach (var window in WindowHelper.ActiveWindows)
            {
                if (window.Content is FrameworkElement root && root.RequestedTheme != ElementTheme.Default)
                    return root.RequestedTheme;
            }
            return Application.Current.RequestedTheme == ApplicationTheme.Dark
                ? ElementTheme.Dark
                : ElementTheme.Light;
        }
    }

    public static ElementTheme RootTheme
    {
        get
        {
            foreach (var window in WindowHelper.ActiveWindows)
            {
                if (window.Content is FrameworkElement root)
                    return root.RequestedTheme;
            }
            return ElementTheme.Default;
        }
        set
        {
            foreach (var window in WindowHelper.ActiveWindows)
            {
                if (window.Content is FrameworkElement root)
                    root.RequestedTheme = value;
            }
        }
    }

    public static void Initialize() { }

    public static bool IsDarkTheme()
    {
        if (RootTheme == ElementTheme.Default)
            return Application.Current.RequestedTheme == ApplicationTheme.Dark;
        return RootTheme == ElementTheme.Dark;
    }
}
