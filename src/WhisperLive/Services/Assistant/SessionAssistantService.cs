using System;
using System.Threading;
using System.Threading.Tasks;
using WhisperLive.Infrastructure;
using WhisperLive.Services.Audio;

namespace WhisperLive.Services.Assistant;

public sealed class SessionAssistantService : ISessionAssistantService, IDisposable
{
    private const string SystemPromptBase =
        "You are a session assistant embedded in a live-transcription desktop app. " +
        "Answer questions about the current session transcript concisely and accurately. " +
        "Respond in 1-5 sentences unless the user asks for a detailed summary, list, email, or minutes. " +
        "You may use Markdown formatting (bold, italic, lists, tables, headings) — it will be rendered. " +
        "The meeting transcript will be provided inline when relevant — do not look for external files.";

    private readonly IRecordingManager _recordingManager;
    private readonly IAiProvider _aiProvider;

    private IAiSession? _chatSession;
    private string? _systemPrompt;
    private CancellationTokenSource? _sessionCts;

    private readonly SemaphoreSlim _sessionCreateLock = new(1, 1);

    public SessionAssistantService(IRecordingManager recordingManager, IAiProvider aiProvider)
    {
        _recordingManager = recordingManager;
        _aiProvider = aiProvider;
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    public void StartSession(string? preContext = null)
    {
        _systemPrompt = BuildSystemPrompt(preContext?.Trim());
        _chatSession = null;
        // Stateful chat: the session accumulates context across questions.
        // This is intentional — the assistant remembers earlier turns in the conversation.
        // Swap to one-shot stateless calls in AskAsync if token growth becomes a concern.
        _sessionCts = new CancellationTokenSource();
    }

    public void EndSession()
    {
        if (_chatSession is null && _systemPrompt is null) return;

        _sessionCts?.Cancel();
        _sessionCts?.Dispose();
        _sessionCts = null;

        _chatSession?.Dispose();
        _chatSession = null;
        _systemPrompt = null;
    }

    // ── Chat ──────────────────────────────────────────────────────────────────

    public async Task<string> AskAsync(string question, AskOptions? options = null, CancellationToken cancellationToken = default)
    {
        // Link caller's token with the session lifecycle token so EndSession() cancels in-flight asks.
        using var linked = _sessionCts is { } cts
            ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, cts.Token)
            : null;
        var ct = linked?.Token ?? cancellationToken;

        var session = await GetOrCreateChatSessionAsync(ct);
        if (session is null) return "No active session.";

        try
        {
            var prompt = BuildPrompt(question, options);
            var context = await BuildCallContextAsync();
            return await session.SendAsync(prompt, context, ct);
        }
        catch (Exception ex)
        {
            AppLogger.Warning(ex, "Session assistant question failed");
            return $"⚠ Couldn't reach {_aiProvider.Name} — {ex.Message}";
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<IAiSession?> GetOrCreateChatSessionAsync(CancellationToken ct)
    {
        if (_chatSession is { } existing) return existing;
        if (_systemPrompt is null) return null;

        await _sessionCreateLock.WaitAsync(ct);
        try
        {
            if (_chatSession is not null) return _chatSession;
            if (_systemPrompt is null) return null;

            _chatSession = _aiProvider.CreateSession(_systemPrompt);
            return _chatSession;
        }
        finally
        {
            _sessionCreateLock.Release();
        }
    }

    private async Task<AiCallContext> BuildCallContextAsync()
    {
        var settings = await AppSettings.LoadAsync();
        return new AiCallContext(
            AllowedReadPaths: settings.AllowedReadPaths,
            ContextPaths: settings.ContextFolderPaths
            // LiveTranscriptPath intentionally omitted — Claude no longer reads the SRT directly.
            // Transcript content is injected inline when AskOptions.IncludeBufferedTranscript is true.
        );
    }

    private string BuildPrompt(string question, AskOptions? options)
    {
        if (options?.IncludeBufferedTranscript != true) return question;

        var segments = _recordingManager.GetRecentSegments();
        if (segments.Count == 0) return question;

        var transcript = string.Join("\n", segments);
        return $"[Transcript — {segments.Count} buffered segments]:\n{transcript}\n\n{question}";
    }

    private static string BuildSystemPrompt(string? preContext)
    {
        var prompt = SystemPromptBase;
        if (preContext is not null)
            prompt += $"\n\nSession context:\n{preContext}";
        return prompt;
    }

    public void Dispose()
    {
        _sessionCts?.Cancel();
        _sessionCts?.Dispose();
        _chatSession?.Dispose();
        _sessionCreateLock.Dispose();
    }
}
