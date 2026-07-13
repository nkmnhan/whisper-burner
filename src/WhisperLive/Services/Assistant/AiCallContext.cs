using System.Collections.Generic;

namespace WhisperLive.Services.Assistant;

/// <summary>
/// Per-call context hints passed to <see cref="IAiSession.SendAsync"/>.
/// Providers map these to their own mechanism:
///   Claude CLI  → --allowedTools (read paths), --add-dir (context folders), live transcript in system prompt
///   OpenAI/Gemini → inject as system message content, or ignore
/// </summary>
public sealed record AiCallContext(
    IReadOnlyList<string>? AllowedReadPaths = null,
    IReadOnlyList<string>? ContextPaths = null,
    string? LiveTranscriptPath = null
);
