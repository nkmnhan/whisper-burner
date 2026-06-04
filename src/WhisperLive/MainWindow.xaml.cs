using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using WhisperLive.Helpers;
using WhisperLive.Views;

namespace WhisperLive;

public sealed partial class MainWindow : Window
{
    private OverlappedPresenter? _windowPresenter;
    private OverlappedPresenterState _currentWindowState;

    public MainWindow()
    {
        InitializeComponent();
        SetWindowProperties();

        RootGrid.ActualThemeChanged += (_, _) =>
            TitleBarHelper.ApplySystemThemeToCaptionButtons(this, RootGrid.ActualTheme);

        // Workaround for WinUI issue #9934: gap between caption buttons and NavigationView
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            _windowPresenter = presenter;
            _currentWindowState = presenter.State;
            AdjustNavigationViewMargin(force: true);
            AppWindow.Changed += (_, _) => AdjustNavigationViewMargin();
        }
    }

    public void Navigate(Type pageType)
    {
        rootFrame.Navigate(pageType);
    }

    private void SetWindowProperties()
    {
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(titleBar);
        AppWindow.SetIcon("Assets/AppIcon.ico");
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
    }

    private void AdjustNavigationViewMargin(bool? force = null)
    {
        if (_windowPresenter is null) return;
        if (_windowPresenter.State == _currentWindowState && force is not true) return;

        NavView.Margin = _windowPresenter.State == OverlappedPresenterState.Maximized
            ? new Thickness(0, -1, 0, 0)
            : new Thickness(0, -2, 0, 0);
        _currentWindowState = _windowPresenter.State;
    }

    private void RootGrid_Loaded(object sender, RoutedEventArgs e)
    {
        WindowHelper.SetWindowMinSize(this, 700, 500);
        TitleBarHelper.ApplySystemThemeToCaptionButtons(this, RootGrid.ActualTheme);
    }

    private void NavView_Loaded(object sender, RoutedEventArgs e)
    {
        NavView.SelectedItem = LiveTranscriptItem;
    }

    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.IsSettingsSelected)
        {
            rootFrame.Navigate(typeof(SettingsPage));
            return;
        }

        if (args.SelectedItem is NavigationViewItem item)
        {
            var pageType = item.Tag?.ToString() switch
            {
                "LiveTranscript" => typeof(LiveTranscriptPage),
                _ => null
            };
            if (pageType is not null)
                rootFrame.Navigate(pageType);
        }
    }

    private void TitleBar_PaneToggleRequested(TitleBar sender, object args)
    {
        NavView.IsPaneOpen = !NavView.IsPaneOpen;
    }

    private void TitleBar_BackRequested(TitleBar sender, object args)
    {
        if (rootFrame.CanGoBack)
            rootFrame.GoBack();
    }
}
