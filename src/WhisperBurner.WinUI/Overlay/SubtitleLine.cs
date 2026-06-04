using System.ComponentModel;

namespace WhisperBurner.WinUI.Overlay;

public sealed class SubtitleLine : INotifyPropertyChanged
{
    private double _fontSize = 28;
    public string Text { get; init; } = "";
    public double FontSize
    {
        get => _fontSize;
        set { _fontSize = value; PropertyChanged?.Invoke(this, new(nameof(FontSize))); }
    }
    public event PropertyChangedEventHandler? PropertyChanged;
}
