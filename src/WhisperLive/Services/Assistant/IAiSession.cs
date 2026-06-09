using System.Threading;
using System.Threading.Tasks;

namespace WhisperLive.Services.Assistant;

/// <summary>
/// A stateful conversation with an AI provider.
/// Implementations retain context across <see cref="SendAsync"/> calls.
/// All mutable state (session ID, locks, retry counters) lives inside the implementation —
/// callers never manage session identity directly.
/// </summary>
public interface IAiSession : IDisposable
{
    /// <summary>
    /// Sends a user message and returns the assistant response.
    /// Thread-safe: implementations serialize concurrent calls internally.
    /// </summary>
    Task<string> SendAsync(string userMessage, AiCallContext? context = null, CancellationToken ct = default);
}
