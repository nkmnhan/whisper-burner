using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Http;
using WhisperLive.Infrastructure;

namespace WhisperLive.Services.Translation.Providers;

/// <summary>
/// Translates text via the DeepL REST API v2.
/// Free tier: 500,000 characters/month, ~50–200ms per request.
/// API key: https://www.deepl.com/pro-api (free tier available).
/// </summary>
public sealed class DeepLTranslationProvider : ITranslationProvider
{
    internal const string ClientName = "translation-deepl";

    private const string FreeApiBase = "https://api-free.deepl.com/v2";
    private const string ProApiBase  = "https://api.deepl.com/v2";

    private readonly IHttpClientFactory _factory;
    private readonly string _apiKey;

    public DeepLTranslationProvider(IHttpClientFactory factory, string apiKey)
    {
        _apiKey = apiKey;
        _factory = factory;
    }

    public string Name => "DeepL";

    public async Task<string> TranslateAsync(string text, string targetLanguage, CancellationToken ct = default)
    {
        using var http = _factory.CreateClient(ClientName);
        var targetCode = MapLanguageCode(targetLanguage);
        var baseUrl = _apiKey.EndsWith(":fx", StringComparison.Ordinal) ? FreeApiBase : ProApiBase;

        using var content = new FormUrlEncodedContent(new[]
        {
            new System.Collections.Generic.KeyValuePair<string, string>("text", text),
            new System.Collections.Generic.KeyValuePair<string, string>("target_lang", targetCode),
        });

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/translate") { Content = content };
        request.Headers.Add("Authorization", $"DeepL-Auth-Key {_apiKey}");

        using var response = await http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<DeepLResponse>(_jsonOptions, ct);
        return result?.Translations?[0]?.Text ?? text;
    }

    // Map ISO 639-1 → DeepL codes (uppercase; some need regional variant).
    private static string MapLanguageCode(string iso) => iso.ToUpperInvariant() switch
    {
        "ZH" => "ZH-HANS",
        "PT" => "PT-PT",
        var c => c,
    };

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    private record DeepLResponse(
        [property: JsonPropertyName("translations")] TranslationEntry[]? Translations);

    private record TranslationEntry(
        [property: JsonPropertyName("text")] string Text,
        [property: JsonPropertyName("detected_source_language")] string DetectedSourceLanguage);
}
