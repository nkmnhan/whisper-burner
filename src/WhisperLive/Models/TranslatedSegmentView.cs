using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using Windows.UI.Text;

namespace WhisperLive.Models;

/// <summary>
/// Observable view-model for a single transcript row.
/// Starts in <see cref="TranslationSegmentState.Provisional"/> (italic, dimmed).
/// Transitions to <see cref="TranslationSegmentState.Translated"/> when the translation
/// arrives — triggering an in-place UI update without re-rendering the whole list.
/// </summary>
public sealed class TranslatedSegmentView : INotifyPropertyChanged
{
    private string? _translatedText;
    private TranslationSegmentState _state = TranslationSegmentState.Provisional;

    public TranslatedSegmentView(SubtitleSegment original)
    {
        Original = original;
    }

    /// <summary>Original Whisper segment — provides Id, Start, End, and original Text.</summary>
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

    /// <summary>Dimmed while pending; further dimmed as secondary line when translated; full opacity otherwise.</summary>
    public double OriginalOpacity =>
        _state == TranslationSegmentState.Provisional ? 0.65 :
        HasTranslation ? 0.50 : 1.0;

    /// <summary>Smaller when a translated line is also showing; normal otherwise.</summary>
    public double OriginalFontSize => HasTranslation ? 12.0 : 14.0;

    /// <summary>Translated text row is visible only when a translation has arrived.</summary>
    public Visibility TranslatedVisibility =>
        HasTranslation ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>
    /// Original text row: hidden when mode=Translated and a translation exists;
    /// always visible otherwise (also acts as the fallback for failed translations).
    /// </summary>
    public Visibility OriginalVisibility => Visibility.Visible;

    // ── Mutation ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Called by <see cref="Services.Translation.TranslationService"/> on the UI thread
    /// when translation completes. Triggers property notifications → in-place row update.
    /// </summary>
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
