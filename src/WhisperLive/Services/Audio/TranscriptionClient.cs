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

namespace WhisperLive.Services.Audio;

public sealed class TranscriptionClient : ITranscriptionClient
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(60) };

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

    public void ResetPrompt() { }

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

        AppLogger.Debug("Transcribing chunk #{Index} ({Bytes} bytes, overlap={Overlap}s)",
            chunk.ChunkIndex, fileBytes.Length, chunk.OverlapSeconds);

        using var response = await _http.PostAsync($"{options.ApiUrl}/transcribe", form, ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct);
        var result = JsonSerializer.Deserialize<TranscribeResponse>(json, _json);

        var segments = result?.Segments?
            .Where(s => !string.IsNullOrWhiteSpace(s.Text))
            .Where(s => !IsHallucination(s.Text))
            .Where(s => s.End > chunk.OverlapSeconds)
            .Select(s => new SubtitleSegment(
                s.Id,
                s.Start + chunk.OffsetSeconds,
                s.End + chunk.OffsetSeconds,
                s.Text.Trim()))
            .ToList() ?? [];

        AppLogger.Debug("Chunk #{Index} → {Count} segment(s)", chunk.ChunkIndex, segments.Count);
        return segments;
    }

    // Whisper hallucinates punctuation-only segments on silence or noise.
    // Bracket/paren annotations like [Music] or (applause) are kept — they carry real context.
    private static bool IsHallucination(string text)
    {
        var t = text.Trim();
        return t.All(c => c is '.' or ',' or '!' or '?' or '-' or '–' or '—' or ' ' or '\u266a' or '\u266b');
    }

    private record TranscribeResponse(string Text, List<SegmentDto> Segments);
    private record SegmentDto(int Id, double Start, double End, string Text);
}
