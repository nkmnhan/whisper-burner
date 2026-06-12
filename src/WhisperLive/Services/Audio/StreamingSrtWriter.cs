using System;
using System.IO;
using System.Text;
using WhisperLive.Infrastructure;
using WhisperLive.Models;

namespace WhisperLive.Services.Audio;

/// <summary>
/// Concrete implementation of <see cref="ISrtSessionWriter"/>.
/// Lazily opens the SRT file on first <see cref="TryWrite"/> call and keeps it open
/// with AutoFlush so every entry is durable to disk immediately.
///
/// Path factory: returns null when the target path is not yet known (e.g. base SRT not created yet).
/// The writer retries on every call until the factory returns a non-null value, then opens once.
/// </summary>
internal sealed class StreamingSrtWriter : ISrtSessionWriter
{
    private readonly Func<string?> _pathFactory;
    private StreamWriter? _writer;

    /// <inheritdoc/>
    public string? CurrentPath { get; private set; }

    public StreamingSrtWriter(Func<string?> pathFactory) => _pathFactory = pathFactory;

    /// <inheritdoc/>
    public bool TryWrite(SubtitleSegment seg)
    {
        if (_writer is null)
        {
            var path = _pathFactory();
            if (path is null) return false;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            _writer = new StreamWriter(path, append: false, Encoding.UTF8) { AutoFlush = true };
            CurrentPath = path;
            AppLogger.Info("SRT writer opened: {Path}", path);
        }

        try
        {
            _writer.WriteLine(seg.ToSrtEntry());
            _writer.WriteLine();
            return true;
        }
        catch (Exception ex)
        {
            AppLogger.Warning(ex, "Failed to write SRT entry to {Path}", CurrentPath!);
            return false;
        }
    }

    public void Dispose()
    {
        _writer?.Flush();
        _writer?.Dispose();
        _writer = null;
    }
}
