# Eager-Placeholder Transcription Pipeline — Design

**Date:** 2026-07-13
**Status:** Approved (pending spec review)
**Component:** WinUI 3 app (`src/WhisperLive/`) — recording → transcription → display path

---

## Problem

During a live session the transcript appears to "freeze" after a few lines. Root
cause (confirmed via `~/whisper.live/logs/app-20260713.log`): the backend runs
~3.9× slower than real-time on CPU-medium, and the pipeline currently keeps only
**one** transcription in flight and **silently drops** every chunk that arrives
while that slot is busy (`RecordingManager.cs:144-148`). Of 22 chunks in the
sample session, only 5 were transcribed and 3 produced segments — hence
"3 lines then nothing." There is no visual signal that audio is being captured
but not transcribed; the UI just looks dead.

## Goal

Replace silent drops with an **eager-placeholder** model: every flushed chunk
immediately produces a visible row. Rows fill in asynchronously as responses
arrive, in the correct time position. When the backend can't keep up, the
transcript degrades **gracefully and visibly** (greyed "skipped" rows) while
staying **near-live** — never drifting far behind current audio.

### Non-goals

- Making the backend faster. Device/model/chunk-size tuning (GPU, `turbo`)
  remains the real throughput fix and is out of scope here. This design makes
  slowness *visible and bounded*, not absent.
- Screen recording (Phase 2).
- Settings UI for the new knobs (they ship as config with sane defaults).

---

## Key constraint (updated)

The Docker server **no longer serializes** transcription. The `_inference_lock`
was removed from `docker/api_server.py` and `NUM_WORKERS` added — faster-whisper
is safe for concurrent `transcribe()` from multiple threads. Consequence:

- On **GPU** (`NUM_WORKERS=2`), concurrent client requests give real parallel
  throughput.
- On **CPU** (`NUM_WORKERS=1`), requests still run one-at-a-time inside the
  model, but client concurrency removes the network/round-trip gap between
  chunks (**pipelining**): chunk N+1 is uploaded and waiting the instant N
  finishes.

Either way, the client should keep a small number of requests in flight rather
than exactly one.

---

## Design

### 1. Concurrency & backlog control

`RecordingManager` replaces the single-slot + drop-guard with a **bounded worker
model**:

| Knob | Default | Meaning |
|---|---|---|
| `TranscriptionWorkers` | **2** | Max transcription requests in flight. |
| `PendingBacklogCap` | **5** | Max placeholders in a non-terminal state (Pending/RetryQueued). |
| `RetryAttempts` | **3** | Total send attempts for a chunk before it becomes `Failed`. |

Flow:

1. `RecordingService` flushes a chunk → `RecordingManager` **immediately** raises
   a `PlaceholderAdded` event (chunk index + start time) and enqueues the chunk
   for transcription.
2. Up to `TranscriptionWorkers` workers pull from the queue and call
   `TranscriptionClient.TranscribeChunkAsync`.
3. **Backlog cap:** if creating a new placeholder would exceed
   `PendingBacklogCap`, the **oldest still-waiting** (not yet in flight)
   placeholder is marked `Skipped` and its chunk dropped from the queue. This
   keeps the backend working on the freshest audio and bounds how far behind
   live the transcript can drift (~`PendingBacklogCap` chunks).

Stale-chunk skipping in `ConsumeChunksAsync` (current `RecordingManager.cs:132-138`)
is removed — the backlog cap subsumes it, and every chunk now gets a row.

### 2. Placeholder lifecycle

A placeholder is keyed by **chunk index** and moves through:

```
Pending ─────────────► Filled            (response: 0 → remove, 1 → fill, N → expand to N rows)
   │                     ▲
   ├──► Skipped          │  (backlog cap; never sent; greyed; main page only; no retry)
   │                     │
   └──► RetryQueued ─────┘  (transient failure; low-priority requeue; success refills in place)
             │
             └──► Failed   (RetryAttempts exhausted; red; main page only; manual tap-to-retry)
```

- **Filled** — the response resolved. 0 segments → the row is removed (brief; it
  was silence). 1 → fills in place. N → the single placeholder **expands into N
  rows** at the correct start-time positions.
- **Skipped** — backlog cap kicked in; the chunk was never sent. Greyed row,
  main page only, no recovery.
- **RetryQueued** — the send failed on a transient error (timeout / 5xx) after
  its in-worker retry. The chunk (with its WAV bytes retained) goes to a
  **low-priority retry queue** drained only when the primary queue has spare
  capacity, so fresh audio always wins (near-live). On success the row refills
  in place.
- **Failed** — `RetryAttempts` exhausted. Red row, main page only, with a
  tap-to-retry affordance that re-enqueues that one chunk.

Only **Filled** (and its translated form) rows ever reach the overlay.

### 3. Two surfaces, one source of truth

`TranscriptViewModel.Segments` remains the single collection. Both surfaces bind
to it; they differ only by filter:

- **Main page (`LiveTranscriptPage`)** — shows **all** rows including
  Pending / Skipped / Failed. This is the working/recovery view.
- **Overlay (`CaptionOverlayWindow`)** — binds through a **filtered view**
  (`State ∈ {Filled, Translated}`). Clean live-caption stream; no placeholder,
  skipped, or failed clutter. Implemented as a filtered/collection view over the
  same `ObservableCollection`, **not** a second collection.

### 4. Data model changes

- `TranslatedSegmentView` gains a `ChunkState` enum
  (`Pending | Filled | Skipped | RetryQueued | Failed`) and a `ChunkIndex`.
  Computed UI properties (visibility, opacity, brush, retry-button visibility)
  derive from it.
- `ChunkState` (transcription lifecycle) is **orthogonal** to the existing
  `TranslationSegmentState` (translation lifecycle) — both live on the row on
  independent axes. A row is first driven by `ChunkState`
  (`Pending → Filled`); once `Filled` it then follows `TranslationSegmentState`
  (`Provisional → Translated/Failed/Passthrough`) exactly as today. They are
  not merged.
- A placeholder row is a `TranslatedSegmentView` created in `Pending` with a
  synthetic start time (chunk offset) and no text.
- On fill, the placeholder is replaced/expanded with the real segment(s);
  ordered insert by `Start` (existing `TranscriptViewModel.cs:36-42`) places late
  arrivals correctly.

### 5. Ordering & dedup

- Ordered-insert-by-`Start` already tolerates out-of-order completion.
- **New risk:** now that *every* chunk is transcribed, adjacent chunks (0.8 s
  overlap) always duplicate text at the boundary, and the current dedup
  (`SubtitleService.StripLeadingOverlap`, keyed off "last 3 appended") can't
  strip against a neighbor that hasn't returned yet.
  **Mitigation:** run overlap-dedup **at insert time against the actual temporal
  predecessor in the list**, regardless of completion order. With
  `TranscriptionWorkers=2` and server-side ordering, completions are near-in-order,
  so residual duplicates are rare and self-correcting.

### 6. Error handling

- Worker catches transient errors (timeout, 5xx, socket) → one immediate retry;
  still failing → `RetryQueued`.
- `RetryQueued` chunks keep their WAV bytes (bounded — only failed rows retain
  audio). Drained at low priority; on success → `Filled`; on exhausting
  `RetryAttempts` → `Failed`.
- Existing `ApiStalled` (3 consecutive hard failures) is retained as a
  higher-level "backend down" signal.

---

## Affected components

| File | Change |
|---|---|
| `Services/Audio/RecordingManager.cs` | Worker pool + backlog cap + retry queue; raise `PlaceholderAdded`; remove single-slot drop-guard and stale-skip. |
| `Services/Audio/IRecordingManager.cs` | Add `PlaceholderAdded` event; add manual `RetryChunkAsync(index)`. |
| `Models/TranslatedSegmentView.cs` | Add `ChunkState`, `ChunkIndex`, derived UI props; keep `TranslationSegmentState` as a separate axis. |
| `Models/ChunkState.cs` (new) | New enum for the transcription lifecycle. |
| `ViewModels/TranscriptViewModel.cs` | `OnPlaceholderAdded`, `OnChunkResolved` (0/1/N), `OnChunkSkipped`, `OnChunkFailed`; expose filtered view for overlay. |
| `Services/Audio/SubtitleService.cs` | Dedup against temporal predecessor at insert. |
| `Views/LiveTranscriptPage.xaml(.cs)` | Row templates/visuals for Pending/Skipped/Failed; tap-to-retry. |
| `Components/CaptionOverlayWindow.xaml(.cs)` | Bind to filtered (Filled/Translated) view. |
| `Infrastructure/AppSettings.cs` | `TranscriptionWorkers`, `PendingBacklogCap`, `RetryAttempts` with defaults. |

---

## Testing

- **Unit:** backlog-cap skip logic (oldest-waiting chosen, in-flight never
  skipped); lifecycle transitions; 0/1/N resolution; dedup against predecessor
  with out-of-order arrival.
- **Integration (existing `test-automation` harness):** run a session against a
  deliberately slow/stubbed backend; assert (a) every chunk yields exactly one
  terminal row state, (b) transcript never lags more than `PendingBacklogCap`
  behind live, (c) overlay shows only filled rows.
- **Manual:** live session on CPU-medium; confirm rows appear immediately as
  placeholders, greyed skips under load, red failures on API kill, refill on
  recovery, clean overlay.

---

## Rollout

1. Server change (lock removed, `NUM_WORKERS`) — **already shipped**.
2. Data model + ViewModel (placeholder lifecycle) — testable in isolation.
3. `RecordingManager` worker pool + backlog + retry.
4. UI visuals + overlay filter.
5. Settings-backed knobs.

Each step builds and passes `dotnet build` before proceeding.
