using System;
using System.Text;
using System.Text.Json;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using WhisperLive.Infrastructure;
using WhisperLive.Models;
using WhisperLive.Services.Audio;

namespace WhisperLive.Services.Assistant;

public sealed class MeetingAssistantService : IMeetingAssistantService, IDisposable
{
    private static readonly TimeSpan NotesRefreshInterval = TimeSpan.FromMinutes(3);
    private const int ThreadCount = 2;

    private const string SystemPromptBase =
        "You are a meeting assistant embedded in a live-transcription desktop app. " +
        "You operate within a persistent session so you retain full transcript history " +
        "across calls — you are never starting from scratch mid-meeting.\n\n" +
        "Two modes of work:\n" +
        "1. Q&A: the user asks a question mid-meeting. Respond in 1-3 sentences max. No preamble.\n" +
        "2. Notes refresh: regenerate consolidated meeting notes as strict JSON with exactly these " +
        "four keys — reasons (why the meeting is happening), goals (what we're trying to achieve), " +
        "approaches (how we plan to get there), decisions (concrete decisions made). " +
        "Each value is a JSON array of short strings. No prose, no markdown fences, no extra keys.\n\n" +
        "The user is mid-meeting. Keep every response brief and actionable.";

    private readonly IRecordingManager _recordingManager;
    private readonly IAiProvider _aiProvider;

    // Per-thread sessions — _sessions[0] is the notes/Thread-1 session (eager),
    // _sessions[1] is Thread 2 (lazy: created on first ask, bootstrapped with full transcript).
    private readonly IAiSession?[] _sessions = new IAiSession?[ThreadCount];

    // Per-thread transcript deltas — both accumulate from StartSession onwards so that
    // Thread 2's first ask can send the full meeting history as bootstrap context.
    private readonly StringBuilder[] _chatDeltas = [new(), new()];

    // Notes delta is separate — only session 0 drives notes refresh.
    private readonly object _bufferLock = new();
    private readonly StringBuilder _notesDelta = new();

    // Latest generated notes — injected into Thread 2 bootstrap so it understands meeting state.
    private MeetingNotes? _latestNotes;
    private string? _systemPrompt;

    private CancellationTokenSource? _sessionCts;

    // Guard lazy creation of session 1 against concurrent first-asks.
    private readonly SemaphoreSlim _sessionCreateLock = new(1, 1);

    public event EventHandler<MeetingNotes>? NotesUpdated;
    public event EventHandler? NotesRefreshStarted;

    public MeetingAssistantService(IRecordingManager recordingManager, IAiProvider aiProvider)
    {
        _recordingManager = recordingManager;
        _aiProvider = aiProvider;
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    public void StartSession(string? preContext = null)
    {
        _systemPrompt = BuildSystemPrompt(preContext?.Trim());
        _sessions[0] = _aiProvider.CreateSession(_systemPrompt);
        _sessions[1] = null; // Thread 2 is lazy

        lock (_bufferLock)
        {
            foreach (var sb in _chatDeltas) sb.Clear();
            _notesDelta.Clear();
            _latestNotes = null;
        }

        _sessionCts = new CancellationTokenSource();
        _recordingManager.SegmentAdded += OnSegmentAdded;
        _ = RunPeriodicNotesRefreshAsync(_sessionCts.Token);
    }

    public void EndSession()
    {
        if (_sessions[0] is null) return;

        _recordingManager.SegmentAdded -= OnSegmentAdded;

        // Cancel in-flight work first so sessions are not disposed under an active call.
        _sessionCts?.Cancel();
        _sessionCts?.Dispose();
        _sessionCts = null;

        for (var i = 0; i < ThreadCount; i++)
        {
            _sessions[i]?.Dispose();
            _sessions[i] = null;
        }

        lock (_bufferLock)
        {
            foreach (var sb in _chatDeltas) sb.Clear();
            _notesDelta.Clear();
            _latestNotes = null;
        }

        _systemPrompt = null;
    }

    private void OnSegmentAdded(object? sender, SubtitleSegment seg)
    {
        lock (_bufferLock)
        {
            // Both threads accumulate — Thread 2's delta grows until its first ask.
            foreach (var sb in _chatDeltas)
                sb.AppendLine(seg.Text);
            _notesDelta.AppendLine(seg.Text);
        }
    }

    // ── Chat ──────────────────────────────────────────────────────────────────

    public async Task<string> AskAsync(
        string question, int threadIndex = 0, CancellationToken cancellationToken = default)
    {
        if ((uint)threadIndex >= ThreadCount)
            throw new ArgumentOutOfRangeException(nameof(threadIndex));

        // Capture session reference locally — safe against EndSession racing disposal.
        var session = await GetOrCreateSessionAsync(threadIndex, cancellationToken);
        if (session is null) return "No active meeting session.";

        // Snapshot delta — restore on failure so context is not lost.
        string delta;
        lock (_bufferLock)
        {
            delta = _chatDeltas[threadIndex].ToString();
            _chatDeltas[threadIndex].Clear();
        }

        var prompt = BuildAskPrompt(question, delta, threadIndex);

        try
        {
            var context = await BuildCallContextAsync();
            return await session.SendAsync(prompt, context, cancellationToken);
        }
        catch (Exception ex)
        {
            // Restore unread delta so the next ask gets the missed transcript.
            lock (_bufferLock)
                _chatDeltas[threadIndex].Insert(0, delta);

            AppLogger.Warning(ex, "Meeting assistant question failed (thread {Index})", threadIndex);
            return $"⚠ Couldn't reach {_aiProvider.Name} — {ex.Message}";
        }
    }

    /// <summary>
    /// Returns the session for <paramref name="threadIndex"/>, creating it lazily for Thread 2.
    /// Thread 2's session is bootstrapped on its own first-ask via <see cref="BuildAskPrompt"/>.
    /// Returns null if the service has been stopped.
    /// </summary>
    private async Task<IAiSession?> GetOrCreateSessionAsync(int threadIndex, CancellationToken ct)
    {
        if (_sessions[threadIndex] is { } existing) return existing;
        if (_systemPrompt is null) return null;

        await _sessionCreateLock.WaitAsync(ct);
        try
        {
            // Double-check after acquiring lock.
            if (_sessions[threadIndex] is not null) return _sessions[threadIndex];
            if (_systemPrompt is null) return null;

            _sessions[threadIndex] = _aiProvider.CreateSession(_systemPrompt);
            return _sessions[threadIndex];
        }
        finally
        {
            _sessionCreateLock.Release();
        }
    }

    /// <summary>
    /// Builds the full prompt for an ask, injecting transcript delta.
    /// For Thread 2's first ask (delta == all transcript since session start),
    /// also injects latest meeting notes so it starts with full meeting context.
    /// </summary>
    private string BuildAskPrompt(string question, string delta, int threadIndex)
    {
        if (delta.Length == 0)
            return $"[Question]\n{question}";

        var sb = new StringBuilder();

        // Thread 2 bootstrap: include notes summary so it understands the meeting state.
        if (threadIndex == 1 && _latestNotes is { } notes)
        {
            sb.AppendLine("[Current meeting notes]");
            AppendNoteSection(sb, "Reasons", notes.Reasons);
            AppendNoteSection(sb, "Goals", notes.Goals);
            AppendNoteSection(sb, "Approaches", notes.Approaches);
            AppendNoteSection(sb, "Decisions", notes.Decisions);
            sb.AppendLine();
        }

        sb.AppendLine("[Meeting transcript since last update]");
        sb.AppendLine(delta);
        sb.AppendLine("[Question]");
        sb.Append(question);
        return sb.ToString();
    }

    private static void AppendNoteSection(StringBuilder sb, string label, IReadOnlyList<string> items)
    {
        if (items.Count == 0) return;
        sb.AppendLine($"{label}: {string.Join("; ", items)}");
    }

    // ── Notes ─────────────────────────────────────────────────────────────────

    public async Task RefreshNotesAsync(CancellationToken cancellationToken = default, bool force = false)
    {
        var session = _sessions[0];
        if (session is null) return;

        string delta;
        lock (_bufferLock)
            delta = _notesDelta.ToString();

        if (delta.Length == 0)
        {
            if (!force) return;
            var recent = _recordingManager.GetRecentSegments();
            delta = string.Join("\n", recent);
            if (delta.Length == 0) return;
        }

        lock (_bufferLock)
            _notesDelta.Remove(0, delta.Length);

        NotesRefreshStarted?.Invoke(this, EventArgs.Empty);
        MeetingNotes? notes;
        try
        {
            notes = await GenerateNotesAsync(session, delta, cancellationToken);
        }
        catch
        {
            lock (_bufferLock)
                _notesDelta.Insert(0, delta);
            throw;
        }

        if (notes is not null)
        {
            lock (_bufferLock)
                _latestNotes = notes;
            NotesUpdated?.Invoke(this, notes);
        }
    }

    private async Task RunPeriodicNotesRefreshAsync(CancellationToken ct)
    {
        try
        {
            using var timer = new PeriodicTimer(NotesRefreshInterval);
            while (await timer.WaitForNextTickAsync(ct))
                await RefreshNotesAsync(ct);
        }
        catch (OperationCanceledException) { }
    }

    private async Task<MeetingNotes?> GenerateNotesAsync(
        IAiSession session, string transcriptDelta, CancellationToken ct)
    {
        var prompt =
            "[New transcript since the last notes update]\n" + transcriptDelta +
            "\n\nRegenerate consolidated meeting notes as JSON (reasons/goals/approaches/decisions). " +
            "Incorporate all transcript so far — not just this delta.";

        try
        {
            var context = await BuildCallContextAsync();
            var response = await session.SendAsync(prompt, context, ct);
            return ParseNotes(response);
        }
        catch (Exception ex)
        {
            AppLogger.Warning(ex, "Meeting notes refresh failed");
            return null;
        }
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
            prompt += $"\n\nContext for this meeting (provided before the session started):\n{preContext}";
        return prompt;
    }

    private static MeetingNotes ParseNotes(string json)
    {
        var trimmed = json.Trim();
        if (trimmed.StartsWith("```"))
        {
            var firstNewline = trimmed.IndexOf('\n');
            var lastFence = trimmed.LastIndexOf("```");
            if (firstNewline >= 0 && lastFence > firstNewline)
                trimmed = trimmed[(firstNewline + 1)..lastFence].Trim();
        }

        try
        {
            using var doc = JsonDocument.Parse(trimmed);
            var root = doc.RootElement;
            return new MeetingNotes(
                ReadStringArray(root, "reasons"),
                ReadStringArray(root, "goals"),
                ReadStringArray(root, "approaches"),
                ReadStringArray(root, "decisions"),
                DateTimeOffset.Now);
        }
        catch (JsonException)
        {
            return new MeetingNotes([trimmed], [], [], [], DateTimeOffset.Now);
        }
    }

    private static IReadOnlyList<string> ReadStringArray(JsonElement root, string property)
    {
        if (!root.TryGetProperty(property, out var arr) || arr.ValueKind != JsonValueKind.Array)
            return [];

        var list = new List<string>();
        foreach (var item in arr.EnumerateArray())
            if (item.GetString() is { Length: > 0 } s)
                list.Add(s);
        return list;
    }

    public void Dispose()
    {
        _sessionCts?.Cancel();
        _sessionCts?.Dispose();
        foreach (var s in _sessions)
            s?.Dispose();
        _sessionCreateLock.Dispose();
    }
}
