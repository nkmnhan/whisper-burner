# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

Containerized OpenAI Whisper ASR with GPU/CPU Docker profiles. Batch-processes videos from `videos/` into SRT transcripts and burnt-in MP4s saved to `videos/output/`.

Also contains a **WinUI 3 desktop app** (`src/WhisperLive/`) for real-time system-audio capture, live subtitle overlay, translation, and session save. Audio-only pipeline: system audio → Whisper API → always-on-top overlay. Screen recording is Phase 2 (not yet implemented).

## Role

Act as a **senior Windows desktop / DevOps developer** and collaborator. Apply clean architecture, strong typing, and practical patterns — no over-engineering.

## Engineering Principles

- **DRY** — extract shared logic, but don't abstract for a single use case
- **KISS** — simplest solution that works; no unnecessary layers
- **YAGNI** — don't build for hypothetical future requirements
- **Fail Fast** — validate at boundaries, throw early with meaningful errors
- **Least Privilege** — minimum permissions; secrets never in code or logs

## Collaboration Protocol

- **Read before write** — always read existing code before modifying; search for similar patterns before creating new ones
- **Evidence over assumption** — when claiming something exists or doesn't, show the grep/glob proof
- **Verify after change** — after modifying code, verify it compiles; never assume correctness

## Build Commands

### WinUI 3 App

```powershell
cd src/WhisperLive

# Regular build
dotnet build -c Debug

# Run
dotnet run -c Debug
```

### WinUI 3 App — Release (self-contained, no admin)

```powershell
.\build-release.cmd
```

### Docker / Batch transcription

```powershell
# GPU (default)
docker compose --profile gpu build
.\process-videos-gpu.cmd

# CPU
docker compose --profile cpu build
.\process-videos-cpu.cmd
```

## Architecture

### Docker pipeline

- `Dockerfile` — Python 3.12-slim, ffmpeg, openai-whisper
- `docker-compose.yml` — `gpu`, `cpu`, `api-gpu`, and `api-cpu` profiles; mounts `./models` and `./videos`
- `process-videos.ps1` — batch transcription + optional translation + subtitle burn
- `videos/` — source files; `videos/output/` — SRT + MP4 outputs

### WinUI 3 App (`src/WhisperLive/`)

Built on **Windows App SDK 2.1 unpackaged**, targeting `net9.0-windows10.0.22621.0`, x64 only.

**App data paths:**
- Settings: `~/whisper.live/settings.json`
- Sessions: `~/whisper.live/sessions/`

**Key packages:**
- `Microsoft.WindowsAppSDK` 2.1.3 — WinUI 3 platform
- `CommunityToolkit.WinUI.Controls.SettingsControls` 8.2 — `SettingsCard`, `SettingsExpander`
- `CommunityToolkit.WinUI.Converters` 8.2 — common XAML converters
- `CommunityToolkit.WinUI.Animations` 8.2 — UI animation helpers
- `Microsoft.Windows.CsWin32` 0.3.269 — source-generated Win32 P/Invoke
- `NAudio` 2.2.1 — loopback capture
- `Serilog` 4.2 + File/Debug sinks — app logging

**Layer structure:**

| Folder | Purpose |
|---|---|
| `App.xaml` / `App.xaml.cs` | Service singletons, `_currentSettings`, `ApplySettings()`, `CaptionOverlayWindow`, `SessionAssistantService(RecordingManager, aiProvider, () => _currentSettings)` |
| `MainWindow.xaml` / `.cs` | `MicaBackdrop`, `TitleBar`, `NavigationView` shell for `LiveTranscriptPage`, `SessionsPage`, `SettingsPage` |
| `Components/` | `CaptionOverlayWindow` (always-on-top, draggable, pause/close buttons, language chips), `GhostButton` |
| `Helpers/` | `ThemeHelper`, `TitleBarHelper`, `WindowHelper`, plus UI helpers like `AssistantMessageTemplateSelector` and `MarkdownHelper` |
| `Infrastructure/` | `AppSettings` (JSON, `~/whisper.live/settings.json`), `AppLogger` (Serilog wrapper) |
| `Models/` | `AssistantMessage`, `AudioChunkInfo`, `CorrectedSegment`, `NotesBubble`, `RecordingOptions`, `RecordingState`, `SavedPrompt`, `SessionNotes`, `SessionSkill`, `SubtitleSegment`, `TranscriptDisplayMode`, `TranslatedSegmentView`, `TranslationSegmentState` |
| `Services/Audio/` | `ISrtSessionWriter` / `StreamingSrtWriter`, `IRecordingService` / `RecordingService`, `ISubtitleService` / `SubtitleService`, `ITranscriptionClient` / `TranscriptionClient`, `IRecordingManager` / `RecordingManager` |
| `Services/Translation/` | `ITranslationService` / `TranslationService`, `DisabledTranslationService`, `SegmentTranslationReadyEventArgs`, translation providers |
| `Services/Assistant/` | `ISessionAssistantService` / `SessionAssistantService`, `IAiProvider` / `ClaudeCliProvider`, `IAiSession`, `IAssistantExportService` / `AssistantExportService`, `AiCallContext`, `AiStreamChunk`, `AskOptions` |
| `Styles/` | `Brushes.xaml` — `ThemeDictionaries` for brushes and shared button styles |
| `ViewModels/` | `TranscriptViewModel` — `ObservableCollection<TranslatedSegmentView>`, translation updates, `FinalizeSession()` |
| `Views/` | `LiveTranscriptPage` (recording, assistant chat, suggestion chips), `SessionsPage` (open/delete saved SRTs), `SettingsPage` (`SettingsCard` / `SettingsExpander`) |

**App shell (WinUI Gallery pattern):**
- `App.xaml.cs` — owns all service singletons; caches `_currentSettings` in memory via `ApplySettings()`; constructs `SessionAssistantService` with `Func<AppSettings>` injection; creates `CaptionOverlayWindow`
- `MainWindow` — `MicaBackdrop`, `TitleBar` (`ExtendsContentIntoTitleBar`), `NavigationView` → `LiveTranscriptPage` / `SessionsPage` / `SettingsPage`

**Data flow (recording session):**
```
LiveTranscriptPage → RecordingManager.StartAsync()
  └─ RecordingService: WasapiLoopbackCapture → Channel<AudioChunkInfo>
  └─ TranscriptionClient.TranscribeChunkAsync() (POST /transcribe)
  └─ SubtitleService.AppendSegments()
       └─ ISrtSessionWriter.TryWrite(seg)          ← streams raw SRT to disk
       └─ SegmentAdded event → RecordingManager
            └─ TranslationService.EnqueueSegment() ← if translation enabled
            └─ TranscriptViewModel.OnSegmentAdded()
  └─ TranslationService (background, 3 concurrent)
       └─ translate → ISrtSessionWriter.TryWrite(seg with translated text)
       └─ SegmentTranslated → TranscriptViewModel.OnSegmentTranslated()
  └─ LiveTranscriptPage binds to TranscriptViewModel.Segments
  └─ CaptionOverlayWindow.ShowSegment() via DispatcherQueue
```

**Session SRT outputs (`~/whisper.live/sessions/`):**
- `yyyy-MM-dd_HH-mm-ss.srt` — raw transcript (streaming write)
- `yyyy-MM-dd_HH-mm-ss.<lang>.srt` — translated transcript (streaming write)
- `yyyy-MM-dd_HH-mm-ss.final.srt` — session-end best snapshot
- `yyyy-MM-dd_HH-mm-ss.corrected.srt` — optional AI-corrected export

**Old project:** `old-one/` — archived prior implementation; reference for business logic only, do not modify.

## Naming Conventions

- Booleans: `is`, `has`, `can`, `should` prefix
- Event handlers: `On` prefix (`OnSegmentAdded`, `OnSegmentTranslated`)
- Async methods: verb prefix (`StartAsync`, `TranscribeChunkAsync`)
- No single-letter names except `i`/`j` in simple loops

## C# / WinUI 3 Conventions

- PascalCase everywhere; `async`/`await` on all I/O paths; nullable enabled
- **UI thread vs background thread**: `RecordingService`, `SubtitleService`, `TranscriptionClient`, and `TranslationService` raise events from background threads. Any event handler in a `Page`/`Window` that touches UI elements must marshal back to the UI thread with `DispatcherQueue.TryEnqueue(() => ...)`. Never move that dispatching into services
- Services are singletons owned by `App`; pages access them via `((App)Application.Current).ServiceName`
- `ISrtSessionWriter` is the shared abstraction for all session SRT streaming writes. `SubtitleService` and `TranslationService` must use `StreamingSrtWriter` through this interface — never create a raw `StreamWriter` for SRT output
- `SessionAssistantService` receives `Func<AppSettings>` via constructor injection. Never call `AppSettings.LoadAsync()` inside assistant service methods — use the injected getter
- Segoe MDL2 Assets glyphs in C# must always use escaped `"\uXXXX"` sequences, never pasted raw Unicode. Verified codepoints: `\uE768`, `\uE769`, `\uE70D`, `\uE70E`, `\uE711`
- `SessionsPage` must read SRT files with `FileShare.ReadWrite` so actively-written session files can be opened safely during recording
- `WindowHelper.TrackWindow()` on every new `Window` — required for `ThemeHelper` to reach all windows
- Custom brushes go in `Styles/Brushes.xaml` under `ThemeDictionaries`, never hardcoded in XAML
- Settings UI uses `SettingsCard` / `SettingsExpander` from `CommunityToolkit.WinUI.Controls`; no raw `Expander` + `StackPanel`
- Win32 P/Invoke: add API names to `NativeMethods.txt` for CsWin32. For interop not covered there (for example DWM attributes), `[DllImport]` is acceptable
- `CancellationTokenSource` per recording session; cancel on stop and on page `Unloaded`
- Never implement UI logic directly in a `Page` or `Window` — delegate to services/view models
- Keep interface and model files under 60 lines — one type per file

## Docker Conventions

- Pin base image to a specific version tag
- One `RUN` layer per logical step; chain with `&&` to minimise layers
- `--no-install-recommends` for apt installs
- Never modify files in `videos/output/` — generated artifacts

## PowerShell Conventions

- PascalCase for functions, camelCase for local variables
- Full cmdlet names — no aliases
- `-LiteralPath` when paths may contain spaces or commas

## Whisper Models

| Model | VRAM | Notes |
|-------|------|-------|
| `turbo` | ~8 GB | Default — fast, accurate |
| `large-v3` | 10–15 GB | Most accurate |

## Development Workflow

1. **Design** — explore requirements before coding
2. **Plan** — step-by-step implementation plan for non-trivial changes
3. **Isolate** — feature branch or git worktree
4. **Execute** — small, verifiable steps
5. **Verify** — `dotnet build` after every C# change
6. **Finish** — merge or PR

## Agent Behaviour

- Run `dotnet build` after every C# change before reporting done
- Always use `-LiteralPath` in PowerShell for `videos/` paths
- Run `docker compose --profile gpu build --no-cache` when Dockerfile changes
- Skip already-processed files (SRT/MP4 exist check) before running docker
- Never implement WinUI 3 features beyond the current phase without user approval
- **NEVER** overwrite `recording.mp4` — session source files are immutable
