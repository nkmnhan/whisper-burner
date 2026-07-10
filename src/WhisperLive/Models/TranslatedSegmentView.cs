using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using Windows.UI.Text;

namespace WhisperLive.Models;

/// <summary>Observable view-model for a single transcript row with translation state.</summary>
public sealed class TranslatedSegmentView : INotifyPropertyChanged
{
    private string? _translatedText;
    private TranslationSegmentState _state = TranslationSegmentState.Provisional;
    private readonly bool _isTranslationEnabled;

    public TranslatedSegmentView(SubtitleSegment original, bool isTranslationEnabled = false)
    {
        Original = original;
        _isTranslationEnabled = isTranslationEnabled;
        // Pre-reserve row height so layout doesn't shift when translation arrives.
        if (isTranslationEnabled)
            _translatedText = "\u00A0";
    }

    /// <summary>The source Whisper segment.</summary>
    public SubtitleSegment Original { get; }

    public string OriginalText => Original.Text;

    public string? TranslatedText
    {
        get => _translatedText;
        private set
        {
            _translatedText = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasTranslation));
            OnPropertyChanged(nameof(TranslatedVisibility));
            OnPropertyChanged(nameof(OriginalVisibility));
            OnPropertyChanged(nameof(OriginalOpacity));
            OnPropertyChanged(nameof(OriginalFontSize));
        }
    }

    public TranslationSegmentState State
    {
        get => _state;
        private set
        {
            _state = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(OriginalFontStyle));
            OnPropertyChanged(nameof(OriginalOpacity));
            OnPropertyChanged(nameof(OriginalFontSize));
            OnPropertyChanged(nameof(HasTranslation));
            OnPropertyChanged(nameof(TranslatedVisibility));
            OnPropertyChanged(nameof(OriginalVisibility));
        }
    }

    // ── Computed UI properties ────────────────────────────────────────────────

    public bool HasTranslation =>
        _state == TranslationSegmentState.Translated && _translatedText is not null;

    /// <summary>Italic while pending, Normal once confirmed, failed, or passthrough.</summary>
    public FontStyle OriginalFontStyle =>
        _state == TranslationSegmentState.Provisional
            ? FontStyle.Italic
            : FontStyle.Normal;

    /// <summary>
    /// Full opacity while awaiting translation so the original is always readable.
    /// Dims to secondary once translation has arrived (original becomes the subtitle).
    /// </summary>
    public double OriginalOpacity =>
        _state == TranslationSegmentState.Provisional
            ? 1.0
            : HasTranslation ? 0.55 : 1.0;

    /// <summary>Smaller in bilingual mode; pre-reserved so layout never shifts on translation.</summary>
    public double OriginalFontSize => _isTranslationEnabled ? 12.0 : 14.0;

    /// <summary>Visible when translation is enabled; collapsed otherwise.</summary>
    public Visibility TranslatedVisibility =>
        _isTranslationEnabled ? Visibility.Visible : Visibility.Collapsed;

    public Visibility OriginalVisibility => Visibility.Visible;

    // ── Mutation ──────────────────────────────────────────────────────────────

    /// <summary>Sets translated text and transitions state to Translated.</summary>
    public void ApplyTranslation(string translated)
    {
        TranslatedText = translated;
        State = TranslationSegmentState.Translated;
    }

    /// <summary>Called when translation fails after all retries.</summary>
    public void MarkFailed()
    {
        State = TranslationSegmentState.Failed;
    }

    /// <summary>Called when translation is disabled — shows original at full opacity, no italic.</summary>
    public void MarkPassthrough()
    {
        State = TranslationSegmentState.Passthrough;
    }

    // ── INotifyPropertyChanged ────────────────────────────────────────────────

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
