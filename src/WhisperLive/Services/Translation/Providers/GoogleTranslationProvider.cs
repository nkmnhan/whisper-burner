using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Http;

namespace WhisperLive.Services.Translation.Providers;

/// <summary>
/// Translates text via the Google Cloud Translation Basic API (v2).
/// Free tier: 500,000 characters/month, ~100–300ms per request.
/// API key: https://cloud.google.com/translate/docs/setup
/// </summary>
public sealed class GoogleTranslationProvider : ITranslationProvider
{
    internal const string ClientName = "translation-google";

    private const string ApiBase = "https://translation.googleapis.com/language/translate/v2";

    private readonly IHttpClientFactory _factory;
    private readonly string _apiKey;

    public GoogleTranslationProvider(IHttpClientFactory factory, string apiKey)
    {
        _factory = factory;
        _apiKey = apiKey;
    }

    public string Name => "Google Translate";

    public async Task<string> TranslateAsync(string text, string targetLanguage, CancellationToken ct = default)
    {
        using var http = _factory.CreateClient(ClientName);
        var url = $"{ApiBase}?key={Uri.EscapeDataString(_apiKey)}" +
                  $"&q={Uri.EscapeDataString(text)}" +
                  $"&target={Uri.EscapeDataString(targetLanguage)}" +
                  "&format=text";

        using var response = await http.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<GoogleResponse>(ct);
        return result?.Data?.Translations?[0]?.TranslatedText ?? text;
    }

    private record GoogleResponse(
        [property: JsonPropertyName("data")] GoogleData? Data);

    private record GoogleData(
        [property: JsonPropertyName("translations")] GoogleTranslation[]? Translations);

    private record GoogleTranslation(
        [property: JsonPropertyName("translatedText")] string TranslatedText);
}
