using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using WhisperLive.Infrastructure;
using WhisperLive.Models;
using WhisperLive.Services.Audio;

namespace WhisperLive.Services.Assistant;

public sealed class SessionAssistantService : ISessionAssistantService, IDisposable
{
    private const string SystemPromptBase =
        "You are a session assistant embedded in a live-transcription desktop app. " +
        "Answer questions about the current session transcript concisely and accurately. " +
        "Respond in 1-5 sentences unless the user asks for a detailed summary, list, email, or minutes. " +
        "You may use Markdown formatting (bold, italic, lists, tables, headings) — it will be rendered. " +
        "When the question needs full context, read the session SRT file provided in your instructions.";

    private readonly IRecordingManager _recordingManager;
    private readonly IAiProvider _aiProvider;

    // Single stateful session for Q&A only — lazy, created on first ask.
    private IAiSession? _chatSession;

    private readonly object _bufferLock = new();
    private readonly StringBuilder _chatDelta = new();
    private string? _systemPrompt;

    private CancellationTokenSource? _sessionCts;

    // Guard lazy creation of the chat session against concurrent first-asks.
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
        _chatSession = null; // created lazily on first ask
        lock (_bufferLock)
            _chatDelta.Clear();
        _sessionCts = new CancellationTokenSource();
        _recordingManager.SegmentAdded += OnSegmentAdded;
    }

    public void EndSession()
    {
        if (_chatSession is null && _systemPrompt is null) return;

        _recordingManager.SegmentAdded -= OnSegmentAdded;

        // Cancel in-flight work first so sessions are not disposed under an active call.
        _sessionCts?.Cancel();
        _sessionCts?.Dispose();
        _sessionCts = null;

        _chatSession?.Dispose();
        _chatSession = null;

        lock (_bufferLock)
            _chatDelta.Clear();
        _systemPrompt = null;
    }

    private void OnSegmentAdded(object? sender, SubtitleSegment seg)
    {
        lock (_bufferLock)
            _chatDelta.AppendLine(seg.Text);
    }

    // ── Chat ──────────────────────────────────────────────────────────────────

    public async Task<string> AskAsync(string question, CancellationToken cancellationToken = default)
    {
        var session = await GetOrCreateChatSessionAsync(cancellationToken);
        if (session is null) return "No active session.";

        string delta;
        lock (_bufferLock)
        {
            delta = _chatDelta.ToString();
            _chatDelta.Clear();
        }

        var prompt = BuildAskPrompt(question, delta);

        try
        {
            var context = await BuildCallContextAsync();
            return await session.SendAsync(prompt, context, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            lock (_bufferLock)
                _chatDelta.Insert(0, delta);
            throw;
        }
        catch (Exception ex)
        {
            lock (_bufferLock)
                _chatDelta.Insert(0, delta);

            AppLogger.Warning(ex, "Session assistant question failed");
            return $"⚠ Couldn't reach {_aiProvider.Name} — {ex.Message}";
        }
    }

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

    private string BuildAskPrompt(string question, string delta)
    {
        if (delta.Length == 0)
            return $"[Question]\n{question}";

        var sb = new StringBuilder();
        sb.AppendLine("[Session transcript]");
        sb.AppendLine(delta);
        sb.AppendLine("[Question]");
        sb.Append(question);
        return sb.ToString();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<AiCallContext> BuildCallContextAsync()
    {
        var settings = await AppSettings.LoadAsync();
        return new AiCallContext(
            AllowedReadPaths: settings.AllowedReadPaths,
            ContextPaths: settings.ContextFolderPaths,
            LiveTranscriptPath: _recordingManager.CurrentSessionPath
        );
    }

    private static string BuildSystemPrompt(string? preContext)
    {
        var prompt = SystemPromptBase;
        if (preContext is not null)
            prompt += $"\n\nContext for this session (provided before it started):\n{preContext}";
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
