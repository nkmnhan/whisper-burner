namespace WhisperLive.Services.Translation;

/// <summary>
/// Event args for <see cref="ITranslationService.SegmentTranslated"/>.
/// Carries the translated text separately so the UI thread can call
/// <see cref="Models.TranslatedSegmentView.ApplyTranslation"/> on its own dispatcher —
/// WinUI 3 INPC must be raised on the UI thread.
/// </summary>
public sealed record SegmentTranslationReadyEventArgs(
    Models.TranslatedSegmentView View,
    string TranslatedText);
