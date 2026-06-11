namespace WhisperLive.Models;

public enum TranslationSegmentState
{
    /// <summary>Translation not yet returned — shown as italic + dimmed.</summary>
    Provisional,

    /// <summary>Translation confirmed — shown at full opacity.</summary>
    Translated,

    /// <summary>Translation failed after retries — original shown as fallback.</summary>
    Failed,
}
