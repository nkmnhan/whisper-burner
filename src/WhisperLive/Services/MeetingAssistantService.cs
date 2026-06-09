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
        "You'll receive fragments of a live meeting transcript as it's captured, plus questions " +
        "or requests to regenerate meeting notes. Be concise and concrete — the user may be " +
        "mid-meeting and needs quick, actionable answers without preamble.";

    private readonly RecordingManager _recordingManager;
    private readonly object _bufferLock = new();
    private readonly StringBuilder _chatDelta = new();
    private readonly StringBuilder _notesDelta = new();
    private readonly SemaphoreSlim _claudeLock = new(1, 1);

    private CancellationTokenSource? _sessionCts;
    private string? _sessionId;

    public event EventHandler<MeetingNotes>? NotesUpdated;

    public MeetingAssistantService(RecordingManager recordingManager)
    {
        _recordingManager = recordingManager;
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    public void StartSession()
    {
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

        var sessionPath = _recordingManager.CurrentSessionPath;
        string? pendingDelta;
        lock (_bufferLock)
        {
            pendingDelta = _notesDelta.Length > 0 ? _notesDelta.ToString() : null;
            _chatDelta.Clear();
            _notesDelta.Clear();
        }

        if (pendingDelta is not null)
            _ = FinalizeNotesAsync(sessionId, sessionPath, pendingDelta);
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

    public async Task RefreshNotesAsync(CancellationToken cancellationToken = default)
    {
        if (_sessionId is not { } sessionId) return;

        string delta;
        lock (_bufferLock)
        {
            delta = _notesDelta.ToString();
            _notesDelta.Clear();
        }

        if (delta.Length == 0) return;

        var notes = await GenerateNotesAsync(sessionId, delta, cancellationToken);
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

    private async Task FinalizeNotesAsync(string sessionId, string? sessionPath, string transcriptDelta)
    {
        var notes = await GenerateNotesAsync(sessionId, transcriptDelta, CancellationToken.None);
        if (notes is null || sessionPath is null) return;

        try
        {
            var notesPath = Path.ChangeExtension(sessionPath, ".notes.md");
            await File.WriteAllTextAsync(notesPath, RenderNotesMarkdown(notes));
            AppLogger.Info("Meeting notes saved: {Path}", notesPath);
        }
        catch (Exception ex)
        {
            AppLogger.Warning(ex, "Failed to save meeting notes file");
        }
    }

    private async Task<MeetingNotes?> GenerateNotesAsync(string sessionId, string transcriptDelta, CancellationToken ct)
    {
        var prompt =
            "[New transcript since the last notes update]\n" + transcriptDelta +
            "\n\nRegenerate the consolidated meeting notes so far as strict JSON with this exact " +
            "shape (no prose, no markdown fences): " +
            "{\"keyPoints\": [\"...\"], \"decisions\": [\"...\"], \"actionItems\": [\"...\"]}";

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
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            return new MeetingNotes(
                ReadStringArray(root, "keyPoints"),
                ReadStringArray(root, "decisions"),
                ReadStringArray(root, "actionItems"),
                DateTimeOffset.Now);
        }
        catch (JsonException)
        {
            // Claude didn't follow the schema — keep the panel populated with raw text
            return new MeetingNotes([json.Trim()], [], [], DateTimeOffset.Now);
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

    private static string RenderNotesMarkdown(MeetingNotes notes)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# Meeting notes — {notes.GeneratedAt:yyyy-MM-dd HH:mm}");
        AppendSection(sb, "Key Points", notes.KeyPoints);
        AppendSection(sb, "Decisions", notes.Decisions);
        AppendSection(sb, "Action Items", notes.ActionItems);
        return sb.ToString();

        static void AppendSection(StringBuilder sb, string title, IReadOnlyList<string> items)
        {
            if (items.Count == 0) return;
            sb.AppendLine().AppendLine($"## {title}");
            foreach (var item in items)
                sb.AppendLine($"- {item}");
        }
    }

    // ── Claude Code subprocess ────────────────────────────────────────────────

    private async Task<string> RunClaudeAsync(string sessionId, string prompt, CancellationToken ct)
    {
        await _claudeLock.WaitAsync(ct);
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "claude",
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            foreach (var arg in await BuildArgumentsAsync(sessionId))
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
                await process.StandardInput.WriteAsync(prompt);
                process.StandardInput.Close();

                var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
                var stderrTask = process.StandardError.ReadToEndAsync(ct);
                await process.WaitForExitAsync(ct);
                var stdout = await stdoutTask;
                var stderr = await stderrTask;

                if (process.ExitCode != 0)
                    throw new InvalidOperationException($"Claude Code exited with an error: {stderr.Trim()}");

                using var doc = JsonDocument.Parse(stdout);
                var root = doc.RootElement;
                if (root.TryGetProperty("is_error", out var isError) && isError.GetBoolean())
                    throw new InvalidOperationException("Claude Code reported an error for this request.");

                return root.TryGetProperty("result", out var result) ? result.GetString() ?? string.Empty : string.Empty;
            }
        }
        finally
        {
            _claudeLock.Release();
        }
    }

    private static string? BuildAllowedTools(AppSettings settings)
    {
        if (settings.AllowedReadPaths.Count > 0)
        {
            var patterns = settings.AllowedReadPaths
                .Select(p => Directory.Exists(p) ? $"Read({p}/**)" : $"Read({p})")
                .ToList();
            return string.Join(",", patterns);
        }
        // Fallback: if a context folder is set but no explicit path list, allow unrestricted reads
        if (!string.IsNullOrWhiteSpace(settings.ContextFolderPath))
            return "Read";
        return null;
    }

    private static async Task<List<string>> BuildArgumentsAsync(string sessionId)
    {
        var args = new List<string> { "-p", "--session-id", sessionId, "--output-format", "json" };
        var settings = await AppSettings.LoadAsync();

        var allowedTools = BuildAllowedTools(settings);
        if (allowedTools is not null)
        {
            args.Add("--allowedTools");
            args.Add(allowedTools);
        }

        var systemPrompt = SystemPromptBase;
        if (!string.IsNullOrWhiteSpace(settings.ContextFolderPath) && Directory.Exists(settings.ContextFolderPath))
        {
            args.Add("--add-dir");
            args.Add(settings.ContextFolderPath);
            systemPrompt += $" You have read access to a project folder at \"{settings.ContextFolderPath}\" — " +
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
