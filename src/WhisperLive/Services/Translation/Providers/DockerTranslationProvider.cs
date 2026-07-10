using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Http;

namespace WhisperLive.Services.Translation.Providers;

/// <summary>Translates via the fast-whisper API (/translate) using deep-translator — no API key required.</summary>
public sealed class DockerTranslationProvider : ITranslationProvider
{
    internal const string ClientName = "translation-docker";

    private readonly IHttpClientFactory _factory;
    private readonly string _apiBase;

    public DockerTranslationProvider(IHttpClientFactory factory, string apiBase)
    {
        _factory = factory;
        _apiBase = apiBase.TrimEnd('/');
    }

    public string Name => "Docker (free)";

    public async Task<string> TranslateAsync(string text, string targetLanguage, CancellationToken ct = default)
    {
        using var http = _factory.CreateClient(ClientName);
        using var content = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("text", text),
            new KeyValuePair<string, string>("target", targetLanguage),
        });

        using var response = await http.PostAsync($"{_apiBase}/translate", content, ct);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<TranslateResponse>(ct);
        return result?.Text ?? text;
    }

    private record TranslateResponse([property: JsonPropertyName("text")] string Text);
}
