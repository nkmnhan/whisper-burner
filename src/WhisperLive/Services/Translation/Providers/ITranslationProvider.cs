using System.Threading;
using System.Threading.Tasks;

namespace WhisperLive.Services.Translation.Providers;

/// <summary>
/// Translates a single sentence to a target language.
/// Implementations: <see cref="DeepLTranslationProvider"/>, <see cref="GoogleTranslationProvider"/>.
/// </summary>
public interface ITranslationProvider
{
    string Name { get; }

    /// <summary>
    /// Translates <paramref name="text"/> into <paramref name="targetLanguage"/> (ISO 639-1 code).
    /// Throws on unrecoverable error; caller handles retries.
    /// </summary>
    Task<string> TranslateAsync(string text, string targetLanguage, CancellationToken ct = default);
}
