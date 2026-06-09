using System.Threading;
using System.Threading.Tasks;

namespace WhisperLive.Services.Assistant;

/// <summary>
/// Abstraction over an AI backend (Claude, OpenAI, Gemini, …).
/// Add a new provider by implementing this interface — no changes to calling code.
/// </summary>
public interface IAiProvider
{
    string Name { get; }

    /// <summary>One-shot completion with no persistent session context.</summary>
    Task<string> CompleteAsync(string prompt, CancellationToken ct = default);

    /// <summary>
    /// Creates a stateful session. The provider manages session identity,
    /// context retention, and retry/rotation internally.
    /// Dispose the session when the recording session ends.
    /// </summary>
    IAiSession CreateSession(string systemPrompt);
}
