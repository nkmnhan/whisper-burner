using System;
using System.Threading;
using System.Threading.Tasks;
using WhisperLive.Infrastructure;
using WhisperLive.Models;

namespace WhisperLive.Services.Translation;

/// <summary>
/// Translation service used when provider = "whisper".
/// Whisper's built-in <c>task=translate</c> already outputs English at transcription time,
/// so each segment is immediately marked as translated without calling any external API.
/// The translated SRT is not written separately — the original SRT already contains English.
/// </summary>
public sealed class PassThroughTranslationService : ITranslationService
{
    public bool IsEnabled => true;

    /// <summary>Whisper translate always outputs English.</summary>
    public string TargetLanguage => "en";

    public event EventHandler<SegmentTranslationReadyEventArgs>? SegmentTranslated;

    public void StartSession()
    {
        AppLogger.Debug("PassThroughTranslationService: session started (Whisper built-in translate)");
    }

    /// <summary>
    /// Fires <see cref="SegmentTranslated"/> with the original text as translation.
    /// Called on the UI thread (from <c>OnSegmentAdded</c> dispatcher), but we still
    /// follow the same pattern — page handler calls <c>ApplyTranslation</c> on its dispatcher.
    /// </summary>
    public void EnqueueSegment(TranslatedSegmentView view)
    {
        SegmentTranslated?.Invoke(this, new SegmentTranslationReadyEventArgs(view, view.OriginalText));
    }

    /// <summary>
    /// No-op: the original SRT already contains the Whisper-translated (English) text.
    /// Writing a duplicate .en.srt would add no value.
    /// </summary>
    public Task WriteTranslatedSrtAsync(string originalSrtPath, CancellationToken ct = default)
    {
        AppLogger.Debug("PassThroughTranslationService: WriteTranslatedSrtAsync skipped — original SRT is already in English");
        return Task.CompletedTask;
    }

    public void EndSession()
    {
        AppLogger.Debug("PassThroughTranslationService: session ended");
    }
}
