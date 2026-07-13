using System.Threading;
using System.Threading.Tasks;

namespace WhisperLive.Services.Translation.Providers;

/// <summary>Translates a single sentence to a target language.</summary>
public interface ITranslationProvider
{
    string Name { get; }

    /// <summary>Translates <paramref name="text"/> to <paramref name="targetLanguage"/> (ISO 639-1). Throws on error; caller retries.</summary>
    Task<string> TranslateAsync(string text, string targetLanguage, CancellationToken ct = default);
}
