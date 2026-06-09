using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WhisperLive.Infrastructure;
using WhisperLive.Models;

namespace WhisperLive.Services;

/// <summary>
/// Shells out to the `claude` CLI in print mode (-p) so meeting questions and
/// running notes ride the user's existing Claude Code login — no Anthropic API
/// key needed. One Claude Code session per recording session (--session-id) lets
/// the model retain transcript context across calls without resending it.
/// </summary>
public sealed class MeetingAssistantService : IMeetingAssistantService, IDisposable
{
    private static readonly TimeSpan NotesRefreshInterval = TimeSpan.FromMinutes(3);

    private const string SystemPromptBase =
        "You are a meeting assistant embedded in a live-transcription desktop app. " +
        "You operate within a persistent Claude Code session (--session-id) so you retain full " +
        "transcript history across calls — you are never starting from scratch mid-meeting.\n\n" +
        "Two modes of work:\n" +
        "1. Q&A: the user asks a question mid-meeting. Respond in 1-3 sentences max. No preamble.\n" +
        "2. Notes refresh: regenerate consolidated meeting notes as strict JSON with exactly these " +
        "four keys — reasons (why the meeting is happening), goals (what we're trying to achieve), " +
        "approaches (how we plan to get there), decisions (concrete decisions made). " +
        "Each value is a JSON array of short strings. No prose, no markdown fences, no extra keys.\n\n" +
        "The user is mid-meeting. Keep every response brief and actionable.";

    private readonly RecordingManager _recordingManager;
    private readonly object _bufferLock = new();
    private readonly StringBuilder _chatDelta = new();
    private readonly StringBuilder _notesDelta = new();
    private readonly SemaphoreSlim _claudeLock = new(1, 1);

    private CancellationTokenSource? _sessionCts;
    private string? _sessionId;
    private string? _preContext;

    public event EventHandler<MeetingNotes>? NotesUpdated;
    public event EventHandler? NotesRefreshStarted;

    public MeetingAssistantService(RecordingManager recordingManager)
    {
        _recordingManager = recordingManager;
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    public void StartSession(string? preContext = null)
    {
        _preContext = string.IsNullOrWhiteSpace(preContext) ? null : preContext.Trim();
        _sessionId = Guid.NewGuid().ToString();
        lock (_bufferLock)
        {
            _chatDelta.Clear();
            _notesDelta.Clear();
        }

        _sessionCts = new CancellationTokenSource();
        _recordingManager.SegmentAdded += OnSegmentAdded;
        _ = RunPeriodicNotesRefreshAsync(_sessionCts.Token);
    }

    public void EndSession()
    {
        if (_sessionId is not { } sessionId) return;

        _recordingManager.SegmentAdded -= OnSegmentAdded;
        _sessionCts?.Cancel();
        _sessionCts?.Dispose();
        _sessionCts = null;
        _sessionId = null;
        _preContext = null;

        lock (_bufferLock)
        {
            _chatDelta.Clear();
            _notesDelta.Clear();
        }
    }

    private void OnSegmentAdded(object? sender, SubtitleSegment seg)
    {
        lock (_bufferLock)
        {
            _chatDelta.AppendLine(seg.Text);
            _notesDelta.AppendLine(seg.Text);
        }
    }

    // ── Chat ──────────────────────────────────────────────────────────────────

    public async Task<string> AskAsync(string question, CancellationToken cancellationToken = default)
    {
        if (_sessionId is not { } sessionId) return "No active meeting session.";

        string delta;
        lock (_bufferLock)
        {
            delta = _chatDelta.ToString();
            _chatDelta.Clear();
        }

        var prompt = delta.Length > 0
            ? $"[New transcript since your last question]\n{delta}\n\n[Question]\n{question}"
            : $"[Question]\n{question}";

        try
        {
            var response = await RunClaudeAsync(sessionId, prompt, cancellationToken);
            return string.IsNullOrWhiteSpace(response) ? "Claude didn't return a response." : response;
        }
        catch (Exception ex)
        {
            AppLogger.Warning(ex, "Meeting assistant question failed");
            return $"⚠ Couldn't reach Claude Code — {ex.Message}";
        }
    }

    // ── Notes ─────────────────────────────────────────────────────────────────

    public async Task RefreshNotesAsync(CancellationToken cancellationToken = default, bool force = false)
    {
        if (_sessionId is not { } sessionId) return;

        string delta;
        lock (_bufferLock)
            delta = _notesDelta.ToString();

        if (delta.Length == 0)
        {
            if (!force) return;
            // Manual refresh with no new delta — resend recent transcript so the user gets a response
            var recent = _recordingManager.GetRecentSegments();
            delta = string.Join("\n", recent);
            if (delta.Length == 0) return;
        }

        // Clear the consumed delta only after we've captured it — restored on failure so no transcript is lost
        lock (_bufferLock)
            _notesDelta.Remove(0, delta.Length);

        NotesRefreshStarted?.Invoke(this, EventArgs.Empty);
        MeetingNotes? notes;
        try
        {
            notes = await GenerateNotesAsync(sessionId, delta, cancellationToken);
        }
        catch
        {
            // Restore delta so the next refresh includes this transcript
            lock (_bufferLock)
                _notesDelta.Insert(0, delta);
            throw;
        }

        if (notes is not null)
            NotesUpdated?.Invoke(this, notes);
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

    private async Task<MeetingNotes?> GenerateNotesAsync(string sessionId, string transcriptDelta, CancellationToken ct)
    {
        var prompt =
            "[New transcript since the last notes update]\n" + transcriptDelta +
            "\n\nRegenerate consolidated meeting notes as JSON (reasons/goals/approaches/decisions). " +
            "Incorporate all transcript so far — not just this delta.";

        try
        {
            var response = await RunClaudeAsync(sessionId, prompt, ct);
            return ParseNotes(response);
        }
        catch (Exception ex)
        {
            AppLogger.Warning(ex, "Meeting notes refresh failed");
            return null;
        }
    }

    private static MeetingNotes ParseNotes(string json)
    {
        // Strip markdown code fences — Claude sometimes wraps JSON despite the prompt
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
            // Claude didn't follow the schema — show raw text in reasons
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

    // ── Claude Code subprocess ────────────────────────────────────────────────

    private async Task<string> RunClaudeAsync(string sessionId, string prompt, CancellationToken ct)
    {
        await _claudeLock.WaitAsync(ct);
        try
        {
            for (var attempt = 0; attempt < 2; attempt++)
            {
                if (attempt == 1)
                    await Task.Delay(1000, ct);

                // Rebuild args each attempt so a rotated sessionId is picked up
                var args = await BuildArgumentsAsync(sessionId);
                var psi = new ProcessStartInfo
                {
                    FileName = "claude",
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardOutputEncoding = System.Text.Encoding.UTF8,
                    StandardErrorEncoding = System.Text.Encoding.UTF8,
                };
                foreach (var arg in args)
                    psi.ArgumentList.Add(arg);

                Process process;
                try
                {
                    process = Process.Start(psi) ?? throw new InvalidOperationException("Process.Start returned null.");
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException("Claude Code CLI not found — is it installed and on PATH?", ex);
                }

                using (process)
                {
                    // Process may exit before reading stdin if session context is too large
                    var stdinFailed = false;
                    try
                    {
                        await process.StandardInput.WriteAsync(prompt);
                        process.StandardInput.Close();
                    }
                    catch (IOException)
                    {
                        stdinFailed = true;
                        try { process.StandardInput.Close(); } catch { }
                    }

                    var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
                    var stderrTask = process.StandardError.ReadToEndAsync(ct);
                    await process.WaitForExitAsync(ct);
                    var stdout = await stdoutTask;
                    var stderr = await stderrTask;

                    if (process.ExitCode != 0 || stdinFailed)
                    {
                        if (attempt == 0)
                        {
                            if (stdinFailed)
                            {
                                // Session context overflowed — rotate to a fresh session
                                _sessionId = Guid.NewGuid().ToString();
                                sessionId = _sessionId;
                                AppLogger.Warning("Session pipe closed (context too large) — rotating to new session ID");
                                continue;
                            }
                            if (stderr.Contains("already in use"))
                            {
                                AppLogger.Warning("Session ID in use — retrying after cooldown");
                                continue;
                            }
                        }
                        throw new InvalidOperationException($"Claude Code exited with an error: {stderr.Trim()}");
                    }

                    using var doc = JsonDocument.Parse(stdout);
                    var root = doc.RootElement;
                    if (root.TryGetProperty("is_error", out var isError) && isError.GetBoolean())
                        throw new InvalidOperationException("Claude Code reported an error for this request.");

                    return root.TryGetProperty("result", out var result) ? result.GetString() ?? string.Empty : string.Empty;
                }
            }
            throw new InvalidOperationException("Claude Code failed after retry.");
        }
        finally
        {
            _claudeLock.Release();
        }
    }

    private static readonly string AppDataFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "whisper.live");

    private static string? BuildAllowedTools(AppSettings settings, string? sessionSrtPath)
    {
        var patterns = new List<string>();

        // Always allow reading the app's own data folder (sessions, settings, logs)
        patterns.Add($"Read({AppDataFolder.Replace('\\', '/')}/**)");

        // Always allow reading the live SRT file when a session is active
        if (sessionSrtPath is not null)
            patterns.Add($"Read({sessionSrtPath.Replace('\\', '/')})");

        // User-configured paths — Claude CLI needs forward slashes for glob matching
        foreach (var p in settings.AllowedReadPaths)
        {
            var fwd = p.Replace('\\', '/');
            patterns.Add(Directory.Exists(p) ? $"Read({fwd}/**)" : $"Read({fwd})");
        }

        if (patterns.Count > 0)
            return string.Join(",", patterns);

        if (settings.ContextFolderPaths.Count > 0)
            return "Read";
        return null;
    }

    private async Task<List<string>> BuildArgumentsAsync(string sessionId)
    {
        var args = new List<string> { "-p", "--session-id", sessionId, "--output-format", "json" };
        var settings = await AppSettings.LoadAsync();

        var sessionSrtPath = _recordingManager.CurrentSessionPath;
        var allowedTools = BuildAllowedTools(settings, sessionSrtPath);
        if (allowedTools is not null)
        {
            args.Add("--allowedTools");
            args.Add(allowedTools);
        }

        var systemPrompt = SystemPromptBase;
        if (_preContext is not null)
            systemPrompt += $"\n\nContext for this meeting (provided before the session started):\n{_preContext}";

        systemPrompt += $"\n\nThe app stores all data under \"{AppDataFolder.Replace('\\', '/')}/\": " +
                        "sessions/ contains SRT transcripts of past meetings, settings.json has user preferences.";

        if (sessionSrtPath is not null)
            systemPrompt += $"\n\nThe current meeting transcript is being written live to \"{sessionSrtPath.Replace('\\', '/')}\". " +
                            "It is an SRT file — read it when you need the complete history of this conversation.";

        foreach (var folder in settings.ContextFolderPaths)
        {
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) continue;
            args.Add("--add-dir");
            args.Add(folder);
            systemPrompt += $"\n\nYou have read access to a project folder at \"{folder.Replace('\\', '/')}\" — " +
                            "consult it when the question relates to code or documents there.";
        }

        args.Add("--append-system-prompt");
        args.Add(systemPrompt);
        return args;
    }

    public void Dispose()
    {
        _sessionCts?.Cancel();
        _sessionCts?.Dispose();
        _claudeLock.Dispose();
    }
}
