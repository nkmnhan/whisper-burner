using System.Threading;
using System.Threading.Tasks;

namespace WhisperLive.Services.Translation.Providers;

/// <summary>
/// Translation provider for Whisper's built-in task=translate mode.
/// Whisper already outputs the target language — no external API call needed.
/// Returns the input text unchanged so TranslationService fires SegmentTranslated
/// with the already-translated text.
/// </summary>
public sealed class PassThroughTranslationProvider : ITranslationProvider
{
    public string Name => "Whisper (built-in)";

    public Task<string> TranslateAsync(string text, string targetLanguage, CancellationToken ct = default)
        => Task.FromResult(text);
}
