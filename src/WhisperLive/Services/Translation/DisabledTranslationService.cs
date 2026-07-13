using System;
using WhisperLive.Models;

namespace WhisperLive.Services.Translation;

/// <summary>
/// Null-object implementation of ITranslationService.
/// Returned by the factory when translation is disabled in settings.
/// All methods are no-ops; no files are written; no events are fired.
/// </summary>
public sealed class DisabledTranslationService : ITranslationService
{
    public string TargetLanguage => string.Empty;
    public bool IsEnabled => false;

    public event EventHandler<SegmentTranslationReadyEventArgs>? SegmentTranslated
    {
        add { }
        remove { }
    }

    public event EventHandler<int>? SegmentTranslationFailed
    {
        add { }
        remove { }
    }

    public void StartSession() { }
    public void EnqueueSegment(SubtitleSegment segment) { }
    public void EndSession() { }
    public void Dispose() { }
}
