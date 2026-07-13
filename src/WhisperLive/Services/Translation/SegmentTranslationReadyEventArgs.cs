namespace WhisperLive.Services.Translation;

/// <summary>
/// Fired on a background thread when a translation is ready.
/// Carries the segment ID so the UI layer can locate its own view-model —
/// the service has no dependency on the presentation layer.
/// </summary>
public sealed record SegmentTranslationReadyEventArgs(int SegmentId, string TranslatedText);
