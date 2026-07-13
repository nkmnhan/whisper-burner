namespace WhisperLive.Models;

public enum TranslationSegmentState
{
    /// <summary>Waiting for translation — italic, dimmed.</summary>
    Provisional,

    /// <summary>Translation confirmed.</summary>
    Translated,

    /// <summary>Translation failed after all retries — show original at full opacity.</summary>
    Failed,

    /// <summary>Translation disabled — show original at full opacity, no italic.</summary>
    Passthrough,
}
