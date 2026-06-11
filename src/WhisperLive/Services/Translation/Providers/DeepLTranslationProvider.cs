using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using WhisperLive.Infrastructure;

namespace WhisperLive.Services.Translation.Providers;

/// <summary>
/// Translates text via the DeepL REST API v2.
/// Free tier: 500,000 characters/month, ~50–200ms per request.
/// API key: https://www.deepl.com/pro-api (free tier available).
/// </summary>
public sealed class DeepLTranslationProvider : ITranslationProvider
{
    private const string FreeApiBase = "https://api-free.deepl.com/v2";
    private const string ProApiBase  = "https://api.deepl.com/v2";

    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(10) };
    private readonly string _apiKey;

    public DeepLTranslationProvider(string apiKey)
    {
        _apiKey = apiKey;
        _http.DefaultRequestHeaders.Add("Authorization", $"DeepL-Auth-Key {apiKey}");
    }

    public string Name => "DeepL";

    public async Task<string> TranslateAsync(string text, string targetLanguage, CancellationToken ct = default)
    {
        // DeepL uses uppercase language codes and regional variants (e.g. "VI", "ZH", "PT-BR").
        var targetCode = MapLanguageCode(targetLanguage);
        var baseUrl = _apiKey.EndsWith(":fx", StringComparison.Ordinal) ? FreeApiBase : ProApiBase;

        using var content = new FormUrlEncodedContent(new[]
        {
            new System.Collections.Generic.KeyValuePair<string, string>("text", text),
            new System.Collections.Generic.KeyValuePair<string, string>("target_lang", targetCode),
        });

        using var response = await _http.PostAsync($"{baseUrl}/translate", content, ct);
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
