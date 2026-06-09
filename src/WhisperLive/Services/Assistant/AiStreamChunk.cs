namespace WhisperLive.Services.Assistant;

/// <summary>
/// Discriminated union for streaming AI responses.
/// Pattern-match on the subtype to handle each event:
///   Text     — a token or chunk of response text (append to display)
///   Thinking — an internal reasoning block (Anthropic extended thinking; display separately)
///   Done     — stream finished; contains the fully assembled response string
/// </summary>
public abstract record AiStreamChunk
{
    public sealed record Text(string Content) : AiStreamChunk;
    public sealed record Thinking(string Content) : AiStreamChunk;
    public sealed record Done(string FullResponse) : AiStreamChunk;
}
