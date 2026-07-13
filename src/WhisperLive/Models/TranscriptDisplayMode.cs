namespace WhisperLive.Models;

/// <summary>
/// Controls which text lines are visible in the transcript panel when translation is active.
/// </summary>
public enum TranscriptDisplayMode
{
    /// <summary>Show original (Whisper) text only.</summary>
    Original,

    /// <summary>Show translated text only (hides original once translation arrives).</summary>
    Translated,

    /// <summary>Show translated text above and original below at reduced size/opacity.</summary>
    Both,
}
