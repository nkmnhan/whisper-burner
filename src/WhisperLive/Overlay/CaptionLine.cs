using System.ComponentModel;

namespace WhisperLive.Overlay;

public sealed class CaptionLine : INotifyPropertyChanged
{
    private string _text = string.Empty;

    public string Text
    {
        get => _text;
        set { _text = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Text))); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
