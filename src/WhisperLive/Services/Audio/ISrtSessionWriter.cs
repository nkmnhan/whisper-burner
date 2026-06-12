using System;
using WhisperLive.Models;

namespace WhisperLive.Services.Audio;

/// <summary>
/// Contract for a lazily-opened, streaming SRT file writer scoped to one recording session.
///
/// Both the raw transcript (SubtitleService) and the translated transcript (TranslationService)
/// depend on this abstraction — ensuring a single, consistent file-writing mechanism.
///
/// Path factory: the caller supplies a Func&lt;string?&gt; that returns null when the target
/// path is not yet known (e.g. raw SRT not yet created). TryWrite retries on every call
/// until the factory returns a non-null path, then opens the file once and keeps it open.
/// </summary>
public interface ISrtSessionWriter : IDisposable
{
    /// <summary>Absolute path of the SRT file; null until the first successful write.</summary>
    string? CurrentPath { get; }

    /// <summary>
    /// Lazily opens the SRT file on first call and appends one entry.
    /// Returns false if the path factory returned null (not ready) or an IO error occurred.
    /// The caller is responsible for text transformation before calling (e.g. translated text).
    /// </summary>
    bool TryWrite(SubtitleSegment seg);
}
