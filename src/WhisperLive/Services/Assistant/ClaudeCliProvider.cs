using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WhisperLive.Infrastructure;

namespace WhisperLive.Services.Assistant;

/// <summary>
/// AI provider backed by the Claude Code CLI (shells out to <c>claude -p</c>).
/// Requires <c>claude</c> to be installed and on PATH with an active login.
/// No Anthropic API key needed — rides the user's existing Claude Code session.
/// </summary>
public sealed class ClaudeCliProvider : IAiProvider
{
    internal static readonly string AppDataFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "whisper.live");

    internal static readonly string GlobalClaudeMdPath = Path.Combine(AppDataFolder, "CLAUDE.md");

    internal static readonly string DefaultGlobalInstructions =
        "# WhisperLive Session Assistant\n\n" +
        "You are a session assistant embedded in a live-transcription desktop app.\n" +
        "Answer questions about the current session transcript concisely and accurately.\n" +
        "Respond in 1–5 sentences unless the user asks for a detailed summary, list, email, or minutes.\n" +
        "You may use Markdown formatting (bold, italic, lists, tables, headings) — it will be rendered.";

    /// <summary>
    /// Writes ~/whisper.live/CLAUDE.md with default content if the file does not yet exist.
    /// Called at app startup so the user always has an editable global instructions file.
    /// </summary>
    internal static async Task EnsureGlobalClaudeMdAsync()
    {
        if (File.Exists(GlobalClaudeMdPath)) return;
        Directory.CreateDirectory(AppDataFolder);
        await File.WriteAllTextAsync(GlobalClaudeMdPath, DefaultGlobalInstructions);
        AppLogger.Info("Created default CLAUDE.md at {Path}", GlobalClaudeMdPath);
    }

    public string Name => "Claude Code CLI";

    /// <summary>
    /// One-shot completion — no session ID, no system prompt.
    /// Used for stateless tasks like transcript correction.
    /// </summary>
    public async Task<string> CompleteAsync(string prompt, CancellationToken ct = default)
    {
        var args = new List<string> { "-p", "--output-format", "json" };
        return await RunProcessAsync(args, prompt, ct);
    }

    public IAiSession CreateSession() =>
        new ClaudeSession();

    // ── Shared subprocess execution ───────────────────────────────────────────

    internal static async Task<string> RunProcessAsync(
        List<string> args, string prompt, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "claude",
            WorkingDirectory = AppDataFolder,
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
            Directory.CreateDirectory(AppDataFolder);
            process = Process.Start(psi)
                ?? throw new InvalidOperationException("Process.Start returned null.");
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                "Claude Code CLI not found — is it installed and on PATH?", ex);
        }

        using (process)
        {
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
            try
            {
                await process.WaitForExitAsync(ct);
            }
            catch (OperationCanceledException)
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                throw;
            }
            var stdout = await stdoutTask;
            var stderr = await stderrTask;

            if (stdinFailed)
                throw new ContextOverflowException();

            if (process.ExitCode != 0)
                throw new InvalidOperationException($"Claude Code exited with error: {stderr.Trim()}");

            return ParseJsonResult(stdout);
        }
    }

    internal static string ParseJsonResult(string stdout)
    {
        using var doc = JsonDocument.Parse(stdout);
        var root = doc.RootElement;
        if (root.TryGetProperty("is_error", out var isError) && isError.GetBoolean())
            throw new InvalidOperationException("Claude Code reported an error for this request.");
        return root.TryGetProperty("result", out var result) ? result.GetString() ?? string.Empty : string.Empty;
    }

    // ── Stateful session ──────────────────────────────────────────────────────

    private sealed class ClaudeSession : IAiSession
    {
        private string _sessionId = Guid.NewGuid().ToString();
        private bool _isFirstTurn = true;
        private string? _conversationSummary; // carried forward on overflow rotation
        private readonly SemaphoreSlim _lock = new(1, 1);

        public ClaudeSession() { }

        public async Task<string> SendAsync(
            string userMessage, AiCallContext? context = null, CancellationToken ct = default)
        {
            await _lock.WaitAsync(ct);
            try
            {
                for (var attempt = 0; attempt < 2; attempt++)
                {
                    var args = BuildArgs(_sessionId, context, _isFirstTurn && _conversationSummary is not null);

                    try
                    {
                        var result = await RunProcessAsync(args, userMessage, ct);
                        _isFirstTurn = false;
                        return result;
                    }
                    catch (ContextOverflowException) when (attempt == 0)
                    {
                        AppLogger.Warning("Claude session pipe closed (context too large) — summarising and rotating to new session ID");
                        _conversationSummary = await TrySummarizeAsync(ct);
                        _sessionId = Guid.NewGuid().ToString();
                        _isFirstTurn = true;
                        continue;
                    }
                    catch (InvalidOperationException ex) when (
                        ex.Message.Contains("already in use") && attempt == 0)
                    {
                        // Claude Code locks a session ID until its process fully releases it.
                        // Retrying with the same ID fails again — rotate to a fresh ID instead.
                        _sessionId = Guid.NewGuid().ToString();
                        AppLogger.Warning("Claude session ID in use — rotating to new session ID and retrying");
                        continue;
                    }
                }

                throw new InvalidOperationException("Claude Code failed after retry.");
            }
            finally
            {
                // Guard against ObjectDisposedException: EndSession() can call Dispose()
                // on the UI thread while this finally block runs on a threadpool thread.
                try { _lock.Release(); } catch (ObjectDisposedException) { }
            }
        }

        /// <summary>
        /// Asks Claude (one-shot, no session) to summarise the current conversation
        /// so the summary can seed a fresh session via --append-system-prompt after rotation.
        /// </summary>
        private async Task<string?> TrySummarizeAsync(CancellationToken ct)
        {
            try
            {
                const string summarizePrompt =
                    "Summarise the conversation we just had in 3–5 concise bullet points so it can be used " +
                    "as context for a fresh session. Focus on questions asked, answers given, and any key " +
                    "decisions or action items surfaced.";
                var args = new List<string> { "-p", "--session-id", _sessionId, "--output-format", "json" };
                var summary = await RunProcessAsync(args, summarizePrompt, ct);
                AppLogger.Info("Conversation summary saved for next session ({Chars} chars)", summary.Length);
                return summary;
            }
            catch (Exception ex)
            {
                AppLogger.Warning(ex, "Could not summarise conversation before rotation — continuing without summary");
                return null;
            }
        }

        private List<string> BuildArgs(string sessionId, AiCallContext? context, bool injectSummary)
        {
            var args = new List<string> { "-p", "--session-id", sessionId, "--output-format", "json" };

            var allowedTools = BuildAllowedTools(context);
            if (allowedTools is not null)
            {
                args.Add("--allowedTools");
                args.Add(allowedTools);
            }

            foreach (var folder in context?.ContextPaths ?? [])
            {
                if (!string.IsNullOrWhiteSpace(folder) && Directory.Exists(folder))
                {
                    args.Add("--add-dir");
                    args.Add(folder);
                }
            }

            // Only inject prior-conversation summary on the first turn after a context rotation.
            // Static persona lives in ~/whisper.live/CLAUDE.md (auto-loaded from working dir).
            // Per-session context lives in ~/whisper.live/session-active/CLAUDE.md (loaded via --add-dir).
            if (injectSummary && _conversationSummary is { } summary)
            {
                args.Add("--append-system-prompt");
                args.Add($"Prior conversation summary (context was rotated due to size):\n{summary}");
            }

            return args;
        }

        private static string? BuildAllowedTools(AiCallContext? context)
        {
            var dataDir = AppDataFolder.Replace('\\', '/');
            var patterns = new List<string>
            {
                $"Read({dataDir}/**)",
                $"Grep({dataDir}/**)",
                $"Glob({dataDir}/**)",
            };

            foreach (var p in context?.AllowedReadPaths ?? [])
            {
                var fwd = p.Replace('\\', '/');
                patterns.Add(Directory.Exists(p) ? $"Read({fwd}/**)" : $"Read({fwd})");
            }

            return string.Join(",", patterns);
        }

        private static string BuildSystemPromptSuffix(string systemPromptBase, AiCallContext? context)
        {
            var prompt = systemPromptBase;

            if (context?.LiveTranscriptPath is { } srtPath && File.Exists(srtPath))
                prompt += $"\n\nCurrent session SRT: \"{srtPath.Replace('\\', '/')}\"";

            foreach (var folder in context?.ContextPaths ?? [])
            {
                if (!string.IsNullOrWhiteSpace(folder))
                    prompt += $"\n\nProject folder: \"{folder.Replace('\\', '/')}\"";
            }

            return prompt;
        }

        public void Dispose()
        {
            // Guard against ObjectDisposedException if Release() is still in-flight
            // on a threadpool thread when EndSession() disposes on the UI thread.
            try { _lock.Dispose(); } catch (ObjectDisposedException) { }
        }
    }

    /// <summary>Signals that stdin was closed by Claude before we finished writing — session context overflowed.</summary>
    private sealed class ContextOverflowException : Exception { }
}
