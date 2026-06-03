# Architecture

## Overview

WinUI 3 desktop app for screen-region recording with live subtitle overlay and deferred burn-in. Runs alongside the existing Docker Whisper stack; the desktop app owns UI and capture while Docker owns transcription.

## Layer map

```
┌──────────────────────────────────────────────┐
│  UI layer (WinUI 3 / XAML)                   │
│  MainWindow → NavigationView                 │
│  RecordingPage  SessionReviewPage  Settings  │
│  SubtitleOverlayWindow (always-on-top)       │
└──────────────┬───────────────────────────────┘
               │ interfaces
┌──────────────▼───────────────────────────────┐
│  Application services                        │
│  IRegionSelectionService                     │
│  IRecordingService                           │
│  ITranscriptionClient  ──► Docker Whisper API│
│  ISubtitleService                            │
│  ISessionRepository                          │
│  ISubtitleBurnService  ──► IFfmpegService    │
└──────────────────────────────────────────────┘
               │
┌──────────────▼───────────────────────────────┐
│  Disk layout                                 │
│  sessions/<id>/recording.mp4                 │
│               subtitles.srt                  │
│               transcript.json                │
│               manifest.json                  │
│               burned/<style>.mp4             │
└──────────────────────────────────────────────┘
```

## Core pipeline (Phase 2+)

1. User selects capture region (`IRegionSelectionService`)
2. Recording starts: screen+audio → MP4 (`IRecordingService`)
3. Audio is chunked (default 3 s) → `AudioChunkReady` event
4. Each chunk sent to Whisper API (`ITranscriptionClient`)
5. Segments merged into subtitle timeline (`ISubtitleService`)
6. `SubtitleOverlayWindow` updated via `SegmentAdded` event
7. SRT written incrementally
8. On stop: session manifest saved (`ISessionRepository`)

## Session model

Each session is an evidence folder. See `SessionManifest` for schema.  
Original `recording.mp4` is **never overwritten**.  
Burns go to `burned/<style>.mp4` and are tracked in `manifest.BurnOutputs`.

## Subtitle overlay window

Separate `Window` instance, always-on-top, borderless, semi-transparent.  
Position is user-draggable. App warns if overlay bounds intersect the capture region.

## Transcription backend

Primary: Docker Whisper API (see `API_CONTRACT.md`).  
Future: local Whisper runtime via same `ITranscriptionClient` abstraction.
