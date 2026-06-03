# Implementation Phases

## Phase 0 — Review-first scaffold (this PR)

- [x] WinUI 3 project structure
- [x] Service interfaces
- [x] Domain models
- [x] Placeholder views and windows
- [x] Architecture and API contract docs
- [x] Decision matrix

No behavioral implementation. Existing Docker/Python workflow untouched.

---

## Phase 1 — MVP capture and persistence

Goal: user can record a screen region to disk and inspect the saved session.

- [ ] `RegionSelectionService` — overlay selector window, returns `CaptureRegion`
- [ ] `RecordingService` — Windows.Graphics.Capture → MP4 via ffmpeg pipe
- [ ] Audio capture — WASAPI mic loopback → PCM chunks
- [ ] `SessionRepository` — creates session folder, writes `manifest.json`
- [ ] Session list visible in `SessionReviewPage`

Acceptance: MP4 + placeholder SRT saved under `sessions/<id>/`.

---

## Phase 2 — Transcription integration

Goal: live floating subtitles during recording.

- [ ] Docker Whisper API wrapper (`ITranscriptionClient` implementation)
- [ ] Health check + model list on app startup
- [ ] Chunked transcription loop (default 3 s) wired to `AudioChunkReady`
- [ ] `SubtitleService` — merge segments, deduplicate, emit `SegmentAdded`
- [ ] `SubtitleOverlayWindow` driven by `SegmentAdded`
- [ ] Overlap warning if overlay window intersects capture region
- [ ] SRT written on session close

Acceptance: subtitles appear in overlay during recording; SRT saved to session folder.

---

## Phase 3 — Review and preview UX

Goal: user can replay session and preview subtitles without burning.

- [ ] Video playback in `SessionReviewPage` (MediaPlayerElement or WebView2)
- [ ] SRT parser → timed subtitle overlay during playback
- [ ] Subtitle timeline/list panel; click row to seek
- [ ] Session detail view (metadata, region, model used)

Acceptance: full replay with subtitle preview in-app.

---

## Phase 4 — Burn-in post-processing

Goal: user can export a burned MP4 from any saved session.

- [ ] `FfmpegService` implementation (wraps existing Docker ffmpeg or local ffmpeg)
- [ ] `SubtitleBurnService` — constructs ffmpeg `-vf subtitles=` command
- [ ] Burn dialog: style picker, output path confirmation
- [ ] Progress bar during burn
- [ ] Output tracked in `manifest.BurnOutputs`
- [ ] Original `recording.mp4` asserted never overwritten

Acceptance: burn produces new file in `sessions/<id>/burned/`; original intact.

---

## Phase 5 — Local engine support (optional)

Goal: user can transcribe without Docker running.

- [ ] `LocalWhisperClient` implementing `ITranscriptionClient`
- [ ] Model download/management UI in Settings
- [ ] Engine selection toggle (Docker API / Local)
- [ ] Fallback behavior when Docker API is unavailable

Acceptance: app transcribes offline using a locally installed model.

---

## Risk log

| Risk | Mitigation |
|---|---|
| Subtitle overlay appears in recording if windows overlap | Warn user; add "snap outside capture area" helper |
| System audio + mic capture complexity | Ship mic-only in Phase 1; system audio as opt-in in Phase 2+ |
| Docker API not running when app starts | Health check on launch; clear status indicator in header |
| Whisper segment revisions across chunks | Deduplicate by time overlap in `SubtitleService.AppendSegments` |
