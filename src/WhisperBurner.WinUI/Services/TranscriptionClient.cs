using System.Net.Http.Json;
using WhisperBurner.WinUI.Infrastructure;
using WhisperBurner.WinUI.Models;

namespace WhisperBurner.WinUI.Services;

public class TranscriptionClient : ITranscriptionClient
{
    // Shared HttpClient — avoids socket exhaustion from per-request instances
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(30) };

    private string BaseUrl => AppSettings.Current.ApiUrl.TrimEnd('/');

    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var r = await _http.GetAsync($"{BaseUrl}/health", cancellationToken);
            return r.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    public async Task<IReadOnlyList<string>> GetAvailableModelsAsync(CancellationToken cancellationToken = default)
    {
        var r = await _http.GetFromJsonAsync<ModelsDto>($"{BaseUrl}/models", cancellationToken);
        return r?.Available ?? [];
    }

    public async Task<IReadOnlyList<SubtitleSegment>> TranscribeChunkAsync(
        Stream audioChunk,
        string model,
        string language,
        CancellationToken cancellationToken = default)
    {
        using var form = new MultipartFormDataContent();
        form.Add(new StreamContent(audioChunk), "file", "chunk.wav");
        form.Add(new StringContent(model), "model");
        form.Add(new StringContent(language), "language");

        var response = await _http.PostAsync($"{BaseUrl}/transcribe", form, cancellationToken);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<TranscribeDto>(
            cancellationToken: cancellationToken);

        return result?.Segments?
            .Select(s => new SubtitleSegment(s.Id, s.Start, s.End, s.Text))
            .ToList() ?? [];
    }

    private record ModelsDto(List<string> Available, string Default);
    private record TranscribeDto(string Text, List<SegmentDto> Segments);
    private record SegmentDto(int Id, double Start, double End, string Text);
}
