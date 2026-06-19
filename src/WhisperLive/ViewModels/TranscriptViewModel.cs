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
            var view = new TranslatedSegmentView(seg, _isTranslationEnabled());

            // Insert at the correct position by Start time so late-arriving chunks
            // from earlier in the session appear in the right place.
            var insertIdx = Segments.Count;
            for (var i = Segments.Count - 1; i >= 0; i--)
            {
                if (Segments[i].Original.Start <= seg.Start) break;
                insertIdx = i;
            }
            Segments.Insert(insertIdx, view);

            while (Segments.Count > MaxSegments)
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

    /// <summary>
    /// Called when the recording session ends. Any segment still in
    /// <see cref="TranslationSegmentState.Provisional"/> state had its translation
    /// cancelled (session CTS fired). Mark them as Failed so the original text
    /// is shown at full opacity instead of staying as an invisible placeholder.
    /// </summary>
    public void FinalizeSession() =>
        _dq.TryEnqueue(() =>
        {
            foreach (var view in Segments)
            {
                if (view.State == TranslationSegmentState.Provisional)
                    view.MarkFailed();
            }
        });

    public void Clear() =>
        _dq.TryEnqueue(() => Segments.Clear());
}
