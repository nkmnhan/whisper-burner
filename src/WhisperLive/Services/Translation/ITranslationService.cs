using System;
using System.Threading;
using System.Threading.Tasks;
using WhisperLive.Models;

namespace WhisperLive.Services.Translation;

public interface ITranslationService
{
    bool IsEnabled { get; }
    string TargetLanguage { get; }

    /// <summary>
    /// Fired on a background thread when a translation is ready.
    /// Handlers MUST marshal to the UI thread before calling
    /// <see cref="Models.TranslatedSegmentView.ApplyTranslation"/> or touching any XAML element.
    /// </summary>
    event EventHandler<SegmentTranslationReadyEventArgs>? SegmentTranslated;

    /// <summary>Prepare a new session. Clears any leftover queue.</summary>
    void StartSession();

    /// <summary>
    /// Enqueues <paramref name="view"/> for translation.
    /// Returns immediately — the view is updated in-place when translation arrives.
    /// Oldest pending items are dropped if the queue is full (live-caption semantics).
    /// </summary>
    void EnqueueSegment(TranslatedSegmentView view);

    /// <summary>
    /// Writes a translated SRT alongside the original.
    /// Path example: original = "session.srt" → translated = "session.vi.srt".
    /// Only segments with confirmed translations are written.
    /// </summary>
    Task WriteTranslatedSrtAsync(string originalSrtPath, CancellationToken ct = default);

    /// <summary>Cancels in-flight translations and drains the queue.</summary>
    void EndSession();
}
