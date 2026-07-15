using System;
using System.IO;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using WhisperLive.Infrastructure;
using WhisperLive.Models;
using WhisperLive.Services.Audio;

namespace WhisperLive.Services.Cli;

/// <summary>
/// Named-pipe server that exposes recording control to CLI scripts.
/// Pipe name: \\.\pipe\whisper-live
/// Protocol: one UTF-8 JSON line in → one UTF-8 JSON line out per connection.
/// Commands: start | stop | pause | resume | status
/// </summary>
public sealed class CliPipeServer : IDisposable
{
    public const string PipeName = "whisper-live";

    private readonly IRecordingManager _manager;
    private readonly Func<AppSettings> _getSettings;
    private readonly Action<AppSettings> _applySettings;
    private readonly CancellationTokenSource _cts = new();

    private static readonly JsonSerializerOptions _json = new()
    {
        PropertyNamingPolicy        = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition      = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented               = false,
    };

    public CliPipeServer(IRecordingManager manager, Func<AppSettings> getSettings, Action<AppSettings> applySettings)
    {
        _manager        = manager;
        _getSettings    = getSettings;
        _applySettings  = applySettings;
        _ = RunAsync(_cts.Token);
        AppLogger.Info("CLI pipe server started on \\\\.\\pipe\\{Pipe}", PipeName);
    }

    private async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var pipe = CreateSecuredPipe();

                await pipe.WaitForConnectionAsync(ct);
                // Sequential: await the handler so the pipe is fully disposed
                // before we create the next NamedPipeServerStream instance.
                await HandleClientAsync(pipe, ct);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { AppLogger.Warning(ex, "CLI pipe accept error"); }
        }
    }

    // The default pipe DACL lets any local process connect and issue commands (start capturing
    // system audio, mutate settings). Restrict access to the current user's SID only.
    private static NamedPipeServerStream CreateSecuredPipe()
    {
        var security = new PipeSecurity();
        using var identity = WindowsIdentity.GetCurrent();
        var owner = identity.User
            ?? throw new InvalidOperationException("Cannot resolve current user SID for pipe ACL.");
        security.AddAccessRule(new PipeAccessRule(owner, PipeAccessRights.FullControl, AccessControlType.Allow));

        return NamedPipeServerStreamAcl.Create(
            PipeName,
            PipeDirection.InOut,
            maxNumberOfServerInstances: 1,
            transmissionMode: PipeTransmissionMode.Byte,
            options: PipeOptions.Asynchronous,
            inBufferSize: 0,
            outBufferSize: 0,
            pipeSecurity: security);
    }

    private async Task HandleClientAsync(NamedPipeServerStream pipe, CancellationToken ct)
    {
        await using (pipe)
        {
            try
            {
                using var reader = new StreamReader(pipe, leaveOpen: true);
                using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };

                var line = await reader.ReadLineAsync(ct);
                if (string.IsNullOrWhiteSpace(line)) return;

                CliRequest? request;
                try { request = JsonSerializer.Deserialize<CliRequest>(line, _json); }
                catch { await WriteAsync(writer, CliResponse.Fail("Invalid JSON")); return; }

                if (request is null) { await WriteAsync(writer, CliResponse.Fail("Empty request")); return; }

                AppLogger.Info("CLI command: {Command}", request.Command);
                var response = await DispatchAsync(request, ct);
                await WriteAsync(writer, response);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { AppLogger.Warning(ex, "CLI pipe handler error"); }
        }
    }

    private async Task<CliResponse> DispatchAsync(CliRequest req, CancellationToken ct)
    {
        return req.Command.ToLowerInvariant() switch
        {
            "start"    => await StartAsync(req),
            "stop"     => await StopAsync(),
            "pause"    => await PauseAsync(),
            "resume"   => await ResumeAsync(),
            "status"   => GetStatus(),
            "settings" => await ApplySettingsAsync(req),
            _          => CliResponse.Fail($"Unknown command '{req.Command}'. Valid: start, stop, pause, resume, status, settings"),
        };
    }

    private async Task<CliResponse> StartAsync(CliRequest req)
    {
        if (_manager.State != RecordingState.Idle)
            return CliResponse.Fail($"Cannot start — current state is '{_manager.State}'");

        var s = _getSettings();
        var options = new RecordingOptions(
            Language:             req.Get("language") ?? s.Language,
            Model:                req.Get("model")    ?? s.Model,
            ChunkDurationSeconds: req.GetInt("chunk") ?? s.ChunkDurationSeconds,
            ApiUrl:               s.ApiUrl);

        try
        {
            await _manager.StartAsync(options);
        }
        catch (RecordingStartException ex)
        {
            return CliResponse.Fail(ex.Message);
        }
        return CliResponse.Success(new { state = _manager.State.ToString(), options });
    }

    private async Task<CliResponse> StopAsync()
    {
        if (_manager.State == RecordingState.Idle)
            return CliResponse.Fail("Not recording");
        await _manager.StopAsync();
        return CliResponse.Success(new { state = _manager.State.ToString() });
    }

    private async Task<CliResponse> PauseAsync()
    {
        if (_manager.State != RecordingState.Recording)
            return CliResponse.Fail($"Cannot pause — state is '{_manager.State}'");
        await _manager.PauseAsync();
        return CliResponse.Success(new { state = _manager.State.ToString() });
    }

    private async Task<CliResponse> ResumeAsync()
    {
        if (_manager.State != RecordingState.Paused)
            return CliResponse.Fail($"Cannot resume — state is '{_manager.State}'");
        await _manager.ResumeAsync();
        return CliResponse.Success(new { state = _manager.State.ToString() });
    }

    private async Task<CliResponse> ApplySettingsAsync(CliRequest req)
    {
        if (_manager.State != RecordingState.Idle)
            return CliResponse.Fail("Cannot change settings while recording — stop first");

        // Clone before mutating: _getSettings() returns the live shared instance that the assistant
        // and view models read from other threads. Patch a private copy, then publish it atomically
        // via _applySettings so readers never observe a half-mutated object.
        var s = _getSettings().Clone();

        // Patch only the fields present in the request args
        if (req.Get("model")              is { } m)   s.Model = m;
        if (req.Get("language")           is { } l)   s.Language = l;
        if (req.GetInt("chunk")           is { } c)   s.ChunkDurationSeconds = c;
        if (req.Get("enableTranslation")  is { } et)  s.EnableTranslation = et.Equals("true", StringComparison.OrdinalIgnoreCase);
        if (req.Get("translationTarget")  is { } tt)  s.TranslationTargetLanguage = tt;
        if (req.Get("translationProvider") is { } tp) s.TranslationProvider = tp;

        await s.SaveAsync();
        _applySettings(s);

        AppLogger.Info("Settings applied via CLI: translation={En} target={Lang} model={Model}",
            s.EnableTranslation, s.TranslationTargetLanguage, s.Model);

        return CliResponse.Success(new
        {
            model               = s.Model,
            language            = s.Language,
            chunk               = s.ChunkDurationSeconds,
            enableTranslation   = s.EnableTranslation,
            translationTarget   = s.TranslationTargetLanguage,
            translationProvider = s.TranslationProvider,
        });
    }

    private CliResponse GetStatus()
    {
        var snap = PipelineMetrics.Instance.GetSnapshot();
        return CliResponse.Success(new
        {
            state          = _manager.State.ToString(),
            sessionPath    = _manager.CurrentSessionPath,
            chunksProcessed  = snap.ChunksProcessed,
            segmentsProduced = snap.SegmentsProduced,
            latency = new
            {
                avgMs = snap.AvgLatencyMs,
                p95Ms = snap.P95LatencyMs,
                minMs = snap.MinLatencyMs,
                maxMs = snap.MaxLatencyMs,
            },
        });
    }

    private static Task WriteAsync(StreamWriter writer, CliResponse response)
        => writer.WriteLineAsync(JsonSerializer.Serialize(response, _json));

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
    }
}

public record CliRequest(
    string Command,
    System.Collections.Generic.Dictionary<string, string>? Args = null)
{
    public string? Get(string key)  => Args?.TryGetValue(key, out var v) == true ? v : null;
    public int?   GetInt(string key) => int.TryParse(Get(key), out var n) ? n : null;
}

public record CliResponse(bool Ok, string? Error, object? Data)
{
    public static CliResponse Fail(string error)        => new(false, error, null);
    public static CliResponse Success(object? data = null) => new(true, null, data);
}
