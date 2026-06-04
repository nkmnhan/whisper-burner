using System.Collections.ObjectModel;
using Microsoft.UI.Xaml.Controls;
using WhisperBurner.WinUI.Models;

namespace WhisperBurner.WinUI.Views;

public sealed partial class TranscriptPanel : UserControl
{
    private readonly ObservableCollection<SubtitleSegment> _items = new();
    private const int MaxLines = 10;

    public TranscriptPanel()
    {
        InitializeComponent();
        Repeater.ItemsSource = _items;
    }

    public void Append(SubtitleSegment segment)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            _items.Add(segment);
            while (_items.Count > MaxLines)
                _items.RemoveAt(0);
            Scroller.ChangeView(null, double.MaxValue, null, disableAnimation: true);
        });
    }

    public void Clear() => DispatcherQueue.TryEnqueue(() => _items.Clear());
}
