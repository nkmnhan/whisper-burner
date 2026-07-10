using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Http;
using WhisperLive.Infrastructure;
using WhisperLive.Models;

namespace WhisperLive.Services.Audio;

public sealed class TranscriptionClient : ITranscriptionClient
{
    internal const string ClientName = "transcription";

    private readonly IHttpClientFactory _factory;

    public TranscriptionClient(IHttpClientFactory factory)
    {
        _factory = factory;
    }

    private static readonly JsonSerializerOptions _json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    public async Task<bool> CheckHealthAsync(string apiUrl, CancellationToken ct = default)
    {
        try
        {
            using var http = _factory.CreateClient(ClientName);
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(3));
            using var response = await http.GetAsync($"{apiUrl}/health", cts.Token);
            return response.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    public void ResetPrompt() { }

    public async Task<IEnumerable<SubtitleSegment>> TranscribeChunkAsync(
        AudioChunkInfo chunk, RecordingOptions options, CancellationToken ct)
    {
        using var http = _factory.CreateClient(ClientName);
        using var form = new MultipartFormDataContent();

        // Chunk is already a complete WAV in memory — no disk read required.
        var fileContent = new ByteArrayContent(chunk.WavData);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
        form.Add(fileContent, "file", $"chunk_{chunk.ChunkIndex:D4}.wav");
        form.Add(new StringContent(options.Language), "language");
        form.Add(new StringContent(options.Model), "model");
        if (!string.IsNullOrEmpty(options.InitialPrompt))
            form.Add(new StringContent(options.InitialPrompt), "initial_prompt");
        if (options.Task != "transcribe")
            form.Add(new StringContent(options.Task), "task");

        AppLogger.Debug("Transcribing chunk #{Index} ({Bytes} bytes, overlap={Overlap}s)",
            chunk.ChunkIndex, chunk.WavData.Length, chunk.OverlapSeconds);

        using var reqCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        reqCts.CancelAfter(TimeSpan.FromSeconds(60));
        using var response = await http.PostAsync($"{options.ApiUrl}/transcribe", form, reqCts.Token);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct);
        var result = JsonSerializer.Deserialize<TranscribeResponse>(json, _json);

        var raw = result?.Segments ?? [];
        foreach (var s in raw.Where(s => s.Text.Contains('_')))
            AppLogger.Warning("Chunk #{Index} — blank token from API: {Text}", chunk.ChunkIndex, s.Text.Trim());

        var segments = raw
            .Where(s => !string.IsNullOrWhiteSpace(s.Text))
            .Where(s => !IsHallucination(s.Text))
            .Where(s => s.Start >= chunk.OverlapSeconds)
            .Select(s => new SubtitleSegment(
                s.Id,
                s.Start + chunk.OffsetSeconds,
                s.End + chunk.OffsetSeconds,
                s.Text.Trim()))
            .ToList();

        AppLogger.Debug("Chunk #{Index} → {Count} segment(s)", chunk.ChunkIndex, segments.Count);
        return segments;
    }

    // Whisper hallucinates punctuation-only segments on silence or noise.
    // Bracket/paren annotations like [Music] or (applause) are kept — they carry real context.
    // faster-whisper emits [BLANK_AUDIO] / [Silence] for silent audio — filter those out too.
    private static readonly HashSet<string> _silenceTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "[BLANK_AUDIO]", "[blank audio]", "[Silence]", "[silence]",
    };

    private static bool IsHallucination(string text)
    {
        var t = text.Trim();
        if (_silenceTokens.Contains(t)) return true;
        if (t.All(c => c == '_' || c == ' ')) return true;
        return t.All(c => c is '.' or ',' or '!' or '?' or '-' or '–' or '—' or ' ' or '\u266a' or '\u266b');
    }

    private record TranscribeResponse(string Text, List<SegmentDto> Segments);
    private record SegmentDto(int Id, double Start, double End, string Text);
}
