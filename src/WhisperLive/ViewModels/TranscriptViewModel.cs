using Microsoft.UI.Dispatching;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using WhisperLive.Models;

namespace WhisperLive.ViewModels;

/// <summary>
/// Single source of truth for the live transcript.
/// Receives raw events from services and mutates <see cref="Segments"/> on the UI thread.
/// Both <see cref="Views.LiveTranscriptPage"/> and <see cref="Overlay.CaptionOverlayWindow"/>
/// bind to this collection — no per-view duplication.
/// </summary>
public sealed class TranscriptViewModel
{
    private const int MaxSegments = 500;
    private readonly DispatcherQueue _dq;
    private readonly Func<bool> _isTranslationEnabled;

    public ObservableCollection<TranslatedSegmentView> Segments { get; } = [];

    public TranscriptViewModel(DispatcherQueue dq, Func<bool> isTranslationEnabled)
    {
        _dq = dq;
        _isTranslationEnabled = isTranslationEnabled;
    }

    public void OnSegmentAdded(SubtitleSegment seg) =>
        _dq.TryEnqueue(() =>
        {
            var view = new TranslatedSegmentView(seg);
            Segments.Add(view);
            if (Segments.Count > MaxSegments)
                Segments.RemoveAt(0);
            if (!_isTranslationEnabled())
                view.MarkPassthrough();
        });

    public void OnSegmentTranslated(int segmentId, string translatedText) =>
        _dq.TryEnqueue(() =>
        {
            var view = Segments.FirstOrDefault(v => v.Original.Id == segmentId);
            view?.ApplyTranslation(translatedText);
        });

    public void Clear() =>
        _dq.TryEnqueue(() => Segments.Clear());
}
