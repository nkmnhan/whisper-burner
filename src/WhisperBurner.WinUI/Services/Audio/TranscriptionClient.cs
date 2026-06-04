using System.Net.Http.Json;
using System.Text.Json.Serialization;
using WhisperBurner.WinUI.Infrastructure;
using WhisperBurner.WinUI.Models;

namespace WhisperBurner.WinUI.Services.Audio;

public class TranscriptionClient : ITranscriptionClient
{
    // Shared HttpClient with no timeout — each call sets its own via CancellationToken
    private static readonly HttpClient _http = new() { Timeout = Timeout.InfiniteTimeSpan };

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

    public async Task<ApiHealthInfo> GetHealthAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var dto = await _http.GetFromJsonAsync<HealthDto>($"{BaseUrl}/health", cancellationToken);
            if (dto == null) return new ApiHealthInfo(false);
            return new ApiHealthInfo(true, dto.LoadedModels?.FirstOrDefault());
        }
        catch { return new ApiHealthInfo(false); }
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

        // Per-request timeout: honour caller token OR TranscribeTimeoutSeconds, whichever fires first
        var timeoutSecs = AppSettings.Current.TranscribeTimeoutSeconds;
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(timeoutSecs));

        var response = await _http.PostAsync($"{BaseUrl}/transcribe", form, cts.Token);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<TranscribeDto>(
            cancellationToken: cts.Token);

        return result?.Segments?
            .Select(s => new SubtitleSegment(s.Id, s.Start, s.End, s.Text))
            .ToList() ?? [];
    }

    private record HealthDto(string Status, bool Gpu,
        [property: JsonPropertyName("loaded_models")] List<string>? LoadedModels);
    private record ModelsDto(List<string> Available, string Default);
    private record TranscribeDto(string Text, List<SegmentDto> Segments);
    private record SegmentDto(int Id, double Start, double End, string Text);
}
