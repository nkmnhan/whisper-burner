using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Input;
using WhisperLive.Infrastructure;

namespace WhisperLive.Views;

/// <summary>View-model for a single session list item.</summary>
public sealed class SessionItemViewModel
{
    public required string FilePath { get; init; }
    public required string DisplayName { get; init; }
    public required string SubTitle { get; init; }
    public required ICommand DeleteCommand { get; init; }
    public required ICommand OpenCommand { get; init; }
}

public sealed partial class SessionsPage : Page
{
    private readonly ObservableCollection<SessionItemViewModel> _items = [];
    private readonly Microsoft.UI.Xaml.Input.XamlUICommand _deleteCommand;
    private readonly Microsoft.UI.Xaml.Input.XamlUICommand _openCommand;
    private bool _dialogOpen;

    public SessionsPage()
    {
        InitializeComponent();

        var deleteCmd = new Microsoft.UI.Xaml.Input.StandardUICommand(
            Microsoft.UI.Xaml.Input.StandardUICommandKind.Delete);
        deleteCmd.ExecuteRequested += OnDeleteExecuteRequested;
        _deleteCommand = deleteCmd;

        var openCmd = new Microsoft.UI.Xaml.Input.XamlUICommand
        {
            Label = "Open",
            Description = "View session transcript",
            IconSource = new Microsoft.UI.Xaml.Controls.FontIconSource { Glyph = "\uE890" }
        };
        openCmd.ExecuteRequested += OnOpenExecuteRequested;
        _openCommand = openCmd;

        SessionsList.ItemsSource = _items;
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e) => await LoadSessionsAsync();

    private async Task LoadSessionsAsync()
    {
        _items.Clear();
        var dir = ((App)Application.Current).SubtitleService.SessionsDirectory;

        if (!Directory.Exists(dir)) { UpdateEmptyState(); return; }

        string[] files;
        try { files = await Task.Run(() => Directory.GetFiles(dir, "*.srt")); }
        catch (Exception ex)
        {
            AppLogger.Warning(ex, "Failed to scan sessions directory");
            UpdateEmptyState();
            return;
        }

        foreach (var filePath in files.OrderByDescending(f => f))
        {
            try
            {
                var fileNameNoExt = Path.GetFileNameWithoutExtension(filePath);
                var displayName = ParseDisplayName(fileNameNoExt);
                var subTitle = await Task.Run(() => BuildSubTitle(filePath));
                _items.Add(new SessionItemViewModel
                {
                    FilePath = filePath,
                    DisplayName = displayName,
                    SubTitle = subTitle,
                    DeleteCommand = _deleteCommand,
                    OpenCommand = _openCommand
                });
            }
            catch (Exception ex)
            {
                AppLogger.Warning(ex, "Skipping session file {Path}", filePath);
            }
        }

        UpdateEmptyState();
    }

    private async void OnDeleteExecuteRequested(
        Microsoft.UI.Xaml.Input.XamlUICommand sender,
        Microsoft.UI.Xaml.Input.ExecuteRequestedEventArgs args)
    {
        if (args.Parameter is not string filePath) return;
        if (_dialogOpen) return;

        // Guard: cannot delete the currently recording session
        var subtitleService = ((App)Application.Current).SubtitleService;
        if (string.Equals(subtitleService.CurrentSessionPath, filePath, StringComparison.OrdinalIgnoreCase))
        {
            _dialogOpen = true;
            try
            {
                var infoDialog = new ContentDialog
                {
                    Title = "Session in use",
                    Content = "This session is currently being recorded. Stop recording before deleting it.",
                    CloseButtonText = "OK",
                    XamlRoot = XamlRoot,
                    Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style
                };
                await infoDialog.ShowAsync();
            }
            finally { _dialogOpen = false; }
            return;
        }

        _dialogOpen = true;
        try
        {
            var confirm = new ContentDialog
            {
                Title = "Delete session?",
                Content = "This will permanently delete the session file. This cannot be undone.",
                PrimaryButtonText = "Delete",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = XamlRoot,
                Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style
            };

            var result = await confirm.ShowAsync();
            if (result != ContentDialogResult.Primary) return;

            try { File.Delete(filePath); }
            catch (Exception ex) { AppLogger.Warning(ex, "Failed to delete session file {Path}", filePath); }

            var item = _items.FirstOrDefault(i =>
                string.Equals(i.FilePath, filePath, StringComparison.OrdinalIgnoreCase));
            if (item is not null) _items.Remove(item);
            UpdateEmptyState();
        }
        finally { _dialogOpen = false; }
    }

    private async void OnOpenExecuteRequested(
        Microsoft.UI.Xaml.Input.XamlUICommand sender,
        Microsoft.UI.Xaml.Input.ExecuteRequestedEventArgs args)
    {
        if (args.Parameter is not string filePath) return;
        if (_dialogOpen) return;

        string content;
        try
        {
            // Use FileShare.ReadWrite so we can read files the TranslationService
            // still has open for streaming writes during an active session.
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var sr = new StreamReader(fs, Encoding.UTF8);
            content = await sr.ReadToEndAsync();
        }
        catch (Exception ex)
        {
            AppLogger.Warning(ex, "Failed to read session file {Path}", filePath);
            content = $"Could not read file:\n{ex.Message}";
        }

        _dialogOpen = true;
        try
        {
            var textBlock = new TextBlock
            {
                Text = content,
                FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"),
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                IsTextSelectionEnabled = true,
            };
            var previewScroll = new ScrollViewer
            {
                Content = textBlock,
                MinHeight = 200,
                MaxHeight = 480,
                Width = 480,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            };

            var dialog = new ContentDialog
            {
                Title = Path.GetFileName(filePath),
                Content = previewScroll,
                CloseButtonText = "Close",
                XamlRoot = XamlRoot,
                Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style
            };

            await dialog.ShowAsync();
        }
        finally { _dialogOpen = false; }
    }

    private void Item_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (e.Pointer.PointerDeviceType is PointerDeviceType.Mouse or PointerDeviceType.Pen)
            VisualStateManager.GoToState(sender as Control, "HoverButtonsShown", true);
    }

    private void Item_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        VisualStateManager.GoToState(sender as Control, "HoverButtonsHidden", true);
    }

    private void UpdateEmptyState()
    {
        var isEmpty = _items.Count == 0;
        EmptyState.Visibility = isEmpty ? Visibility.Visible : Visibility.Collapsed;
        SessionsList.Visibility = isEmpty ? Visibility.Collapsed : Visibility.Visible;
        SubHeaderText.Text = isEmpty ? "" : $"{_items.Count} saved sessions";
    }

    private static string ParseDisplayName(string fileNameNoExt)
    {
        // File names can have known suffixes: "yyyy-MM-dd_HH-mm-ss[.suffix]"
        // e.g. "2026-06-12_09-25-20"         → June 12, 2026 · 9:25 AM
        //      "2026-06-12_09-25-20.final"    → June 12, 2026 · 9:25 AM · final
        //      "2026-06-12_09-25-20.vi"       → June 12, 2026 · 9:25 AM · vi
        //      "2026-06-12_09-25-20.corrected"→ June 12, 2026 · 9:25 AM · corrected
        var dotIdx = fileNameNoExt.IndexOf('.');
        var basePart = dotIdx >= 0 ? fileNameNoExt[..dotIdx] : fileNameNoExt;
        var suffix = dotIdx >= 0 ? fileNameNoExt[(dotIdx + 1)..] : null;

        if (!DateTime.TryParseExact(basePart, "yyyy-MM-dd_HH-mm-ss",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
            return fileNameNoExt;

        var dateStr = dt.ToString("MMMM d, yyyy · h:mm tt");
        return suffix is null ? dateStr : $"{dateStr} · {suffix}";
    }

    private static string BuildSubTitle(string filePath)
    {
        try
        {
            var info = new FileInfo(filePath);
            var sizeKb = info.Length / 1024.0;

            // Use FileShare.ReadWrite so actively-written translation SRT files can be read.
            string[] lines;
            using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var sr = new StreamReader(fs, Encoding.UTF8))
                lines = sr.ReadToEnd().Split('\n');

            // Count valid SRT blocks: a numeric-only line followed by a line containing " --> "
            var entryCount = 0;
            for (var i = 0; i < lines.Length - 1; i++)
            {
                if (int.TryParse(lines[i].Trim(), out _) && lines[i + 1].Contains(" --> "))
                    entryCount++;
            }

            return entryCount > 0
                ? $"{entryCount} entries · {sizeKb:F1} KB"
                : $"{sizeKb:F1} KB";
        }
        catch { return ""; }
    }
}
