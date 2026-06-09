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

    public IAiSession CreateSession(string systemPrompt) =>
        new ClaudeSession(systemPrompt);

    // ── Shared subprocess execution ───────────────────────────────────────────

    internal static async Task<string> RunProcessAsync(
        List<string> args, string prompt, CancellationToken ct)
    {
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
            await process.WaitForExitAsync(ct);
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
        private readonly string _systemPromptBase;
        private string _sessionId = Guid.NewGuid().ToString();
        private readonly SemaphoreSlim _lock = new(1, 1);

        public ClaudeSession(string systemPromptBase) =>
            _systemPromptBase = systemPromptBase;

        public async Task<string> SendAsync(
            string userMessage, AiCallContext? context = null, CancellationToken ct = default)
        {
            await _lock.WaitAsync(ct);
            try
            {
                for (var attempt = 0; attempt < 2; attempt++)
                {

                    var args = BuildArgs(_sessionId, _systemPromptBase, context);

                    try
                    {
                        return await RunProcessAsync(args, userMessage, ct);
                    }
                    catch (ContextOverflowException) when (attempt == 0)
                    {
                        _sessionId = Guid.NewGuid().ToString();
                        AppLogger.Warning("Claude session pipe closed (context too large) — rotating to new session ID");
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
                _lock.Release();
            }
        }

        private static List<string> BuildArgs(
            string sessionId, string systemPromptBase, AiCallContext? context)
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

            var systemPrompt = BuildSystemPromptSuffix(systemPromptBase, context);
            args.Add("--append-system-prompt");
            args.Add(systemPrompt);

            return args;
        }

        private static string? BuildAllowedTools(AiCallContext? context)
        {
            var patterns = new List<string>
            {
                $"Read({AppDataFolder.Replace('\\', '/')}/**)"
            };

            if (context?.LiveTranscriptPath is { } srt)
                patterns.Add($"Read({srt.Replace('\\', '/')})");

            foreach (var p in context?.AllowedReadPaths ?? [])
            {
                var fwd = p.Replace('\\', '/');
                patterns.Add(Directory.Exists(p) ? $"Read({fwd}/**)" : $"Read({fwd})");
            }

            // Always emit — AppDataFolder read is always included
            return string.Join(",", patterns);
        }

        private static string BuildSystemPromptSuffix(string systemPromptBase, AiCallContext? context)
        {
            var prompt = systemPromptBase;

            prompt += $"\n\nThe app stores all data under \"{AppDataFolder.Replace('\\', '/')}/\": " +
                      "sessions/ contains SRT transcripts of past meetings, settings.json has user preferences.";

            if (context?.LiveTranscriptPath is { } srt)
                prompt += $"\n\nThe current meeting transcript is being written live to " +
                          $"\"{srt.Replace('\\', '/')}\". It is an SRT file — read it when you need the complete history.";

            foreach (var folder in context?.ContextPaths ?? [])
            {
                if (!string.IsNullOrWhiteSpace(folder))
                    prompt += $"\n\nYou have read access to a project folder at " +
                              $"\"{folder.Replace('\\', '/')}\" — consult it when the question relates to code or documents there.";
            }

            return prompt;
        }

        public void Dispose() => _lock.Dispose();
    }

    /// <summary>Signals that stdin was closed by Claude before we finished writing — session context overflowed.</summary>
    private sealed class ContextOverflowException : Exception { }
}
