using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WhisperLive.Infrastructure;
using WhisperLive.Models;

namespace WhisperLive.Services;

public sealed class TranscriptionClient : ITranscriptionClient
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(60) };

    // Rolling context fed to Whisper as initial_prompt — last ~200 chars of transcribed text.
    // Helps Whisper handle boundary words and maintain consistent spelling/terminology.
    // Must be called on the same thread as TranscribeChunkAsync (serial consumer only).
    private string _lastPrompt = string.Empty;

    private static readonly JsonSerializerOptions _json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    public async Task<bool> CheckHealthAsync(string apiUrl, CancellationToken ct = default)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(3));
            using var response = await _http.GetAsync($"{apiUrl}/health", cts.Token);
            return response.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    public void ResetPrompt() => _lastPrompt = string.Empty;

    public async Task<IEnumerable<SubtitleSegment>> TranscribeChunkAsync(
        AudioChunkInfo chunk, RecordingOptions options, CancellationToken ct)
    {
        using var form = new MultipartFormDataContent();

        var fileBytes = await File.ReadAllBytesAsync(chunk.FilePath, ct);
        var fileContent = new ByteArrayContent(fileBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
        form.Add(fileContent, "file", Path.GetFileName(chunk.FilePath));
        form.Add(new StringContent(options.Language), "language");
        form.Add(new StringContent(options.Model), "model");
        if (_lastPrompt.Length > 0)
            form.Add(new StringContent(_lastPrompt), "initial_prompt");

        AppLogger.Debug("Transcribing chunk #{Index} ({Bytes} bytes, overlap={Overlap}s, prompt={PromptLen} chars)",
            chunk.ChunkIndex, fileBytes.Length, chunk.OverlapSeconds, _lastPrompt.Length);

        using var response = await _http.PostAsync($"{options.ApiUrl}/transcribe", form, ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct);
        var result = JsonSerializer.Deserialize<TranscribeResponse>(json, _json);

        var segments = result?.Segments?
            .Where(s => !string.IsNullOrWhiteSpace(s.Text))
            .Where(s => s.Start >= chunk.OverlapSeconds)   // skip overlap region already covered by previous chunk
            .Select(s => new SubtitleSegment(
                s.Id,
                s.Start + chunk.OffsetSeconds,
                s.End + chunk.OffsetSeconds,
                s.Text.Trim()))
            .ToList() ?? [];

        // Update rolling prompt: keep last ~200 chars so next chunk has context
        if (result?.Text is { Length: > 0 } text)
        {
            var combined = _lastPrompt + " " + text.Trim();
            _lastPrompt = combined.Length > 200
                ? combined[^200..]
                : combined;
        }

        AppLogger.Debug("Chunk #{Index} → {Count} segment(s)", chunk.ChunkIndex, segments.Count);
        return segments;
    }

    private record TranscribeResponse(string Text, List<SegmentDto> Segments);
    private record SegmentDto(int Id, double Start, double End, string Text);
}
