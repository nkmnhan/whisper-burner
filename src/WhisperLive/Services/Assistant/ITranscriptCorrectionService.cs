using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using WhisperLive.Models;

namespace WhisperLive.Services.Assistant;

public interface ITranscriptCorrectionService
{
    event EventHandler<IReadOnlyList<CorrectedSegment>>? BatchCorrected;
    void StartSession();
    Task FlushAsync(CancellationToken ct = default);
    void EndSession();
}
