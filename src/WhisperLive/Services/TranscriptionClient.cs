using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WhisperLive.Models;

namespace WhisperLive.Services;

public sealed class TranscriptionClient : ITranscriptionClient
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(60) };

    private static readonly JsonSerializerOptions _json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

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

        var response = await _http.PostAsync($"{options.ApiUrl}/transcribe", form, ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct);
        var result = JsonSerializer.Deserialize<TranscribeResponse>(json, _json);

        return result?.Segments?
            .Where(s => !string.IsNullOrWhiteSpace(s.Text))
            .Select(s => new SubtitleSegment(
                s.Id,
                s.Start + chunk.OffsetSeconds,
                s.End + chunk.OffsetSeconds,
                s.Text.Trim()))
            ?? [];
    }

    private record TranscribeResponse(string Text, List<SegmentDto> Segments);
    private record SegmentDto(int Id, double Start, double End, string Text);
}
