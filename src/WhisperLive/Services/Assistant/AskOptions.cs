namespace WhisperLive.Services.Assistant;

/// <summary>
/// Per-ask options for <see cref="ISessionAssistantService.AskAsync"/>.
/// Use an options record instead of positional booleans so new options can be
/// added without breaking existing call sites.
/// </summary>
public sealed record AskOptions(
    /// <summary>
    /// When <see langword="true"/>, the buffered (in-memory) transcript segments
    /// are injected inline into the prompt. Useful for summary or analysis questions
    /// where the model must reason over the current session content.
    /// Note: this is the <em>recent</em> in-memory buffer, not the full persisted SRT.
    /// </summary>
    bool IncludeBufferedTranscript = false
);
