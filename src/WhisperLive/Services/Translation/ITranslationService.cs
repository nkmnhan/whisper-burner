using System;
using WhisperLive.Models;

namespace WhisperLive.Services.Translation;

public interface ITranslationService
{
    /// <summary>ISO 639-1 target language code (e.g. "vi", "zh", "en").</summary>
    string TargetLanguage { get; }

    /// <summary>
    /// Whether translation is active. False for <see cref="DisabledTranslationService"/>.
    /// Callers may use this to decide UI state (e.g. show/hide translation chip).
    /// </summary>
    bool IsEnabled { get; }

    /// <summary>
    /// Fired on a background thread when a translation is ready.
    /// Handlers MUST marshal to the UI thread before touching any XAML element.
    /// </summary>
    event EventHandler<SegmentTranslationReadyEventArgs>? SegmentTranslated;

    /// <summary>Prepare a new session. Clears any leftover queue and resets the SRT writer.</summary>
    void StartSession();

    /// <summary>
    /// Enqueues <paramref name="segment"/> for translation.
    /// Returns immediately. The translated SRT is written incrementally as results arrive.
    /// </summary>
    void EnqueueSegment(SubtitleSegment segment);

    /// <summary>
    /// Ends the session: cancels in-flight work, writes any remaining segments
    /// (with original-text fallback for untranslated), and closes the writer.
    /// </summary>
    void EndSession();
}
