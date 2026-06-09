# Design: AI Transcript Correction + Meeting Assistant Permissions

**Date:** 2026-06-09  
**Status:** Approved  
**Branch:** feat/winui3-desktop-app

---

## Overview

Three related improvements to the Meeting Assistant feature:

1. **Bug Fix A** — Pre-authorize file reads so `--add-dir` actually works without interactive permission prompts  
2. **Bug Fix B** — Serialize concurrent Claude subprocess calls to prevent "session ID already in use" errors  
3. **New: Allowed Read Paths settings** — User-configured list of files/folders Claude may read; more secure than unrestricted  
4. **New: `TranscriptCorrectionService`** — Batch AI correction of clipped/duplicate words every 3 minutes; produces a `.corrected.srt` alongside the raw `.srt`

---

## Fix A — File Read Permissions

**Problem:** `claude -p --add-dir <path>` adds the directory as context but Claude Code still blocks file reads without an interactive permission prompt. The live app has no TTY so the prompt is never shown and the call fails.

**Fix:** In `MeetingAssistantService.BuildArgumentsAsync()`, when constructing the argument list, add `--allowedTools` based on the `AllowedReadPaths` setting:

- If `AllowedReadPaths` is non-empty: add `--allowedTools "Read(path1),Read(path2),..."` where each folder path is suffixed with `\**` and each file path is used as-is
- If `AllowedReadPaths` is empty but `ContextFolderPath` is set: fall back to `--allowedTools "Read"` (unrestricted — same implicit trust as today)
- If neither is set: no `--allowedTools` argument added

Path type is resolved at call time: `Directory.Exists(path)` → append `\**`; `File.Exists(path)` → use exact path.

---

## Fix B — Session ID Concurrency

**Problem:** `MeetingAssistantService` can call `RunClaudeAsync` concurrently from two sources — the 3-minute `PeriodicTimer` tick (notes refresh) and a user-submitted question. Claude Code rejects a second request on the same `--session-id` while one is already in flight.

**Fix:** Add `SemaphoreSlim _claudeLock = new(1, 1)` to `MeetingAssistantService`. Every call to `RunClaudeAsync` must acquire the semaphore before spawning the process and release it in a `finally` block. This serializes all meeting-session Claude calls; the second caller waits rather than failing.

---

## Allowed Read Paths — Settings UI

### AppSettings change

```csharp
public List<string> AllowedReadPaths { get; set; } = [];
```

Plain folder/file paths — no glob syntax stored. Globs constructed at call time.

### SettingsPage UI

Added as a new `SettingsExpander` in the existing **AI Assistant** section. **Implementation note:** reuse the exact same icons, XAML patterns, and code structure already present in `SettingsPage.xaml` / `SettingsPage.xaml.cs` — do not introduce new control styles or helper patterns. The existing `SettingsExpander` + `SettingsCard` usage is the reference.

```
╔══════════════════════════════════════════════════════════╗
║  Allowed Read Paths                          ▼ expanded  ║
║  Files and folders Claude can read                       ║
╠══════════════════════════════════════════════════════════╣
║                                                          ║
║  📁 C:\Users\...\Projects\moa-knowledge          [✕]    ║
║  📄 C:\Users\...\Documents\specs.md              [✕]    ║
║                                                          ║
║  [📁 Add folder]  [📄 Add file]                          ║
║                                                          ║
╚══════════════════════════════════════════════════════════╝
```

**XAML structure** — follows existing `SettingsPage.xaml` patterns exactly:
- `SettingsExpander` with `FontIcon` glyph `&#xE8F4;` (FolderOpen)
- The expanded area contains a `ListView` bound to `ObservableCollection<string> _allowedPaths` with an `ItemTemplate` showing a `TextBlock` (path, `TextTrimming="CharacterEllipsis"`, `TextFillColorSecondaryBrush`) + remove `Button` (glyph `&#xE711;` — Cancel/X, same as used elsewhere in the Gallery)
- Below the list: `StackPanel Orientation="Horizontal" Spacing="8"` containing `[📁 Add folder]` and `[📄 Add file]` buttons
- Icon prefix (📁/📄) per row determined by `Directory.Exists` at bind time via the `ItemTemplate`

**Code-behind** — follows `OnBrowseContextFolderClicked` pattern exactly:
- `FolderPicker` with `InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(window))`
- `FileOpenPicker` (single file, `FileTypeFilter.Add("*")`) using the same `InitializeWithWindow` pattern
- Both handlers append the selected path to `_allowedPaths` and `_settings.AllowedReadPaths`, then call `_ = _settings.SaveAsync()`
- Remove button handler: removes from both collections, calls `_ = _settings.SaveAsync()`
- `OnLoaded` populates `_allowedPaths` from `_settings.AllowedReadPaths`

---

## TranscriptCorrectionService

### Responsibility

Subscribes to raw `SubtitleSegment` events, batches them for 3 minutes, asks Claude to correct clipped/duplicate words, and emits corrected text back to the UI and subtitle file.

### Interface

```csharp
public interface ITranscriptCorrectionService
{
    event EventHandler<IReadOnlyList<CorrectedSegment>>? BatchCorrected;
    void StartSession(string? sessionPath);
    Task FlushAsync(CancellationToken ct = default);
    void EndSession();
}

public record CorrectedSegment(int OriginalId, string CorrectedText);
```

### Lifecycle

- `App.xaml.cs` creates `TranscriptCorrectionService` as a singleton, passing `RecordingManager` and `SubtitleService`
- `RecordingManager.StartAsync()` calls `CorrectionService.StartSession()` — no path needed; `.corrected.srt` path is derived from `SubtitleService.CurrentSessionPath` lazily when the first correction is written
- `RecordingManager.StopAsync()` calls `CorrectionService.EndSession()` (which triggers a final flush before returning)

### Per-tick behaviour (every 3 minutes)

1. Drain the segment buffer (atomic swap — clear under lock, process the drained copy)
2. If buffer is empty, skip
3. Build prompt:
   ```
   You are a transcript corrector. Fix ONLY these problems in each line:
   - Trailing "..." or "-" indicating a clipped word — reconstruct the word from context
   - Duplicate words at the start of a line that already appeared at the end of the previous line — remove the duplicate
   Do not rephrase, summarize, or change meaning. Return a JSON array of corrected strings,
   one entry per input line, in the same order.
   Input lines:
   ["raw line 1", "raw line 2", ...]
   ```
4. Call `claude -p --output-format json` — **no `--session-id`** (each batch is self-contained)
5. Parse JSON array response; map back to original segment IDs
6. Call `SubtitleService.ApplyCorrections()` directly (injected dependency) — writes `.corrected.srt`
7. Fire `BatchCorrected` event with `IReadOnlyList<CorrectedSegment>` — UI subscribers update the live transcript
8. On parse failure, log warning and skip — raw transcript is unaffected

### Separate Claude session

Correction calls use no `--session-id`, so they start fresh each batch. This avoids polluting the Q&A meeting session with correction prompts and keeps each batch cheap (no prior history to re-send).

### `--allowedTools`

Correction calls do not read files — they only process text passed in the prompt. No `--allowedTools` needed.

---

## SubtitleService — `.corrected.srt`

`SubtitleService` gets a new method:

```csharp
public void ApplyCorrections(IReadOnlyList<CorrectedSegment> corrections);
```

- Looks up each `OriginalId` in `_segments`
- Updates the in-memory `Text` on the matching segment
- Opens `{CurrentSessionPath}.corrected.srt` lazily on first call (same deferred-open pattern as the raw `.srt` writer), then appends all corrected entries
- If `CurrentSessionPath` is still null (no segments yet), corrections are held in memory and flushed when `EnsureCorrectedFile()` is called on the next write

The raw `.srt` writer is never touched — raw output is immutable once written.

---

## Live Transcript Panel — Corrected Text Display

`LiveTranscriptPage` subscribes to `TranscriptCorrectionService.BatchCorrected`.

When a batch arrives:
1. For each `CorrectedSegment`, find the matching item in `_segments` (matched by position — segments are appended in order so index = `OriginalId - 1`)
2. Replace the text in `_segments` at that index
3. `ObservableCollection` change notification refreshes the `ListView` automatically

The overlay (`CaptionOverlayWindow`) is **not** updated — it shows live raw captions only. Corrected text appears in the main window transcript and the `.corrected.srt` file.

---

## Data Flow Summary

```
RecordingService → AudioChunkInfo
  └─ TranscriptionClient.TranscribeChunkAsync()
       └─ SubtitleService.AppendSegments()
            ├─ writes raw .srt (unchanged)
            ├─ fires SegmentAdded → RecordingManager → LiveTranscriptPage (raw display)
            └─ fires SegmentAdded → TranscriptCorrectionService buffer

TranscriptCorrectionService (every 3 min)
  └─ claude -p (no session-id) → corrected JSON array
       └─ BatchCorrected event
            ├─ LiveTranscriptPage: updates _segments in-place → ObservableCollection refresh
            └─ SubtitleService.ApplyCorrections() → writes .corrected.srt
```

---

## Files Changed

| File | Change |
|------|--------|
| `Infrastructure/AppSettings.cs` | Add `AllowedReadPaths: List<string>` |
| `Services/IMeetingAssistantService.cs` | No change |
| `Services/MeetingAssistantService.cs` | Add `SemaphoreSlim`; update `BuildArgumentsAsync` for scoped/unrestricted `--allowedTools` |
| `Services/ITranscriptCorrectionService.cs` | **New** — interface + `CorrectedSegment` record |
| `Services/TranscriptCorrectionService.cs` | **New** — implementation |
| `Services/SubtitleService.cs` | Add `ApplyCorrections()`; open `.corrected.srt` lazily |
| `App.xaml.cs` | Add `TranscriptCorrectionService` singleton; wire `StartSession`/`EndSession` calls |
| `Views/SettingsPage.xaml` | Add Allowed Read Paths `SettingsExpander` with list + two pickers |
| `Views/SettingsPage.xaml.cs` | `ObservableCollection<string>` for paths; `FolderPicker`/`FileOpenPicker` handlers |
| `Views/LiveTranscriptPage.xaml.cs` | Subscribe to `BatchCorrected`; update `_segments` in-place |

---

## Out of Scope

- Whisper `initial_prompt` API changes (server-side fix for clipping at source)
- Overlay showing corrected text
- Showing diff between raw and corrected in the UI
- Per-session correction on/off toggle
