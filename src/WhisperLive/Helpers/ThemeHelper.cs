using Microsoft.UI.Xaml;
using System.Threading.Tasks;
using WhisperLive.Infrastructure;

namespace WhisperLive.Helpers;

public static partial class ThemeHelper
{
    public static ElementTheme ActualTheme
    {
        get
        {
            foreach (Window window in WindowHelper.ActiveWindows)
            {
                if (window.Content is FrameworkElement rootElement)
                {
                    if (rootElement.RequestedTheme != ElementTheme.Default)
                        return rootElement.RequestedTheme;
                }
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
            foreach (Window window in WindowHelper.ActiveWindows)
            {
                if (window.Content is FrameworkElement rootElement)
                    return rootElement.RequestedTheme;
            }
            return ElementTheme.Default;
        }
        set
        {
            foreach (Window window in WindowHelper.ActiveWindows)
            {
                if (window.Content is FrameworkElement rootElement)
                    rootElement.RequestedTheme = value;
            }
        }
    }

    // Restores saved theme from AppSettings on launch (gallery pattern)
    public static async Task InitializeAsync()
    {
        var settings = await AppSettings.LoadAsync();
        RootTheme = settings.Theme switch
        {
            "Light" => ElementTheme.Light,
            "Dark" => ElementTheme.Dark,
            _ => ElementTheme.Default,
        };
    }

    public static bool IsDarkTheme()
    {
        if (RootTheme == ElementTheme.Default)
            return Application.Current.RequestedTheme == ApplicationTheme.Dark;
        return RootTheme == ElementTheme.Dark;
    }
}
