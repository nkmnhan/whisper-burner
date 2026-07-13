using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WhisperLive.Infrastructure;
using WhisperLive.Services.Audio;

namespace WhisperLive.Services.Assistant;

public sealed class SessionAssistantService : ISessionAssistantService, IDisposable
{
    private static readonly string SessionActiveFolder =
        Path.Combine(ClaudeCliProvider.AppDataFolder, "session-active");

    private static readonly string SessionClaudeMdPath =
        Path.Combine(SessionActiveFolder, "CLAUDE.md");

    /// <summary>Max recent segments injected inline per ask — keeps per-turn tokens bounded.</summary>
    private const int MaxInlineSegments = 50;

    private readonly IRecordingManager _recordingManager;
    private readonly IAiProvider _aiProvider;
    private readonly Func<AppSettings> _getSettings;

    private IAiSession? _chatSession;
    private CancellationTokenSource? _sessionCts;

    private readonly SemaphoreSlim _sessionCreateLock = new(1, 1);

    public SessionAssistantService(
        IRecordingManager recordingManager,
        IAiProvider aiProvider,
        Func<AppSettings> getSettings)
    {
        _recordingManager = recordingManager;
        _aiProvider = aiProvider;
        _getSettings = getSettings;
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    public void StartSession(string? preContext = null)
    {
        EndSession();
        WriteSessionClaudeMd(preContext?.Trim());
        _sessionCts = new CancellationTokenSource();
    }

    public void EndSession()
    {
        if (_chatSession is null && _sessionCts is null) return;

        _sessionCts?.Cancel();
        _sessionCts?.Dispose();
        _sessionCts = null;

        _chatSession?.Dispose();
        _chatSession = null;

        TryDeleteSessionClaudeMd();
    }

    // ── Chat ──────────────────────────────────────────────────────────────────

    public async Task<string> AskAsync(string question, AskOptions? options = null, CancellationToken cancellationToken = default)
    {
        // EndSession() can dispose _sessionCts between our null-check and accessing .Token.
        // Treat ObjectDisposedException the same as "no active session".
        CancellationTokenSource? linked = null;
        try
        {
            linked = _sessionCts is { } cts
                ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, cts.Token)
                : null;
        }
        catch (ObjectDisposedException)
        {
            return "No active session.";
        }

        using (linked)
        {
            var ct = linked?.Token ?? cancellationToken;
            var session = await GetOrCreateChatSessionAsync(ct);
            if (session is null) return "No active session.";

            try
            {
                var prompt = BuildPrompt(question, options);
                var context = BuildCallContext();
                return await session.SendAsync(prompt, context, ct);
            }
            catch (Exception ex)
            {
                AppLogger.Warning(ex, "Session assistant question failed");
                return $"⚠ Couldn't reach {_aiProvider.Name} — {ex.Message}";
            }
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<IAiSession?> GetOrCreateChatSessionAsync(CancellationToken ct)
    {
        if (_chatSession is { } existing) return existing;
        if (_sessionCts is null) return null;

        await _sessionCreateLock.WaitAsync(ct);
        try
        {
            if (_chatSession is not null) return _chatSession;
            if (_sessionCts is null) return null;

            _chatSession = _aiProvider.CreateSession();
            return _chatSession;
        }
        finally
        {
            _sessionCreateLock.Release();
        }
    }

    private AiCallContext BuildCallContext()
    {
        var settings = _getSettings();
        var contextPaths = new System.Collections.Generic.List<string> { SessionActiveFolder };
        contextPaths.AddRange(settings.ContextFolderPaths);
        return new AiCallContext(
            AllowedReadPaths: settings.AllowedReadPaths,
            ContextPaths: contextPaths,
            LiveTranscriptPath: _recordingManager.CurrentSessionPath
        );
    }

    private string BuildPrompt(string question, AskOptions? options)
    {
        if (options?.IncludeBufferedTranscript != true) return question;

        var all = _recordingManager.GetRecentSegments();
        if (all.Count == 0) return question;

        // Cap at MaxInlineSegments to keep per-turn token usage bounded.
        var segments = all.Count > MaxInlineSegments
            ? all.Skip(all.Count - MaxInlineSegments).ToList()
            : all;

        var transcript = string.Join("\n", segments);
        var note = all.Count > MaxInlineSegments
            ? $"[Transcript — last {MaxInlineSegments} of {all.Count} segments]"
            : $"[Transcript — {segments.Count} segment(s)]";

        return $"{note}:\n{transcript}\n\n{question}";
    }

    // ── Session CLAUDE.md ─────────────────────────────────────────────────────

    private static void WriteSessionClaudeMd(string? preContext)
    {
        try
        {
            Directory.CreateDirectory(SessionActiveFolder);
            var content = string.IsNullOrEmpty(preContext)
                ? string.Empty
                : $"# Session Context\n\n{preContext}";
            File.WriteAllText(SessionClaudeMdPath, content);
        }
        catch (Exception ex)
        {
            AppLogger.Warning(ex, "Could not write session CLAUDE.md");
        }
    }

    private static void TryDeleteSessionClaudeMd()
    {
        try { File.Delete(SessionClaudeMdPath); }
        catch { /* best-effort */ }
    }

    public void Dispose()
    {
        _sessionCts?.Cancel();
        _sessionCts?.Dispose();
        _chatSession?.Dispose();
        _sessionCreateLock.Dispose();
    }
}
