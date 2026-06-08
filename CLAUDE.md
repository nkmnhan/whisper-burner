# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

Containerized OpenAI Whisper ASR with GPU/CPU Docker profiles. Batch-processes videos from `videos/` into SRT transcripts and burnt-in MP4s saved to `videos/output/`.

Also contains a **WinUI 3 desktop app** (`src/WhisperBurner.WinUI/`) for system-audio capture, live subtitle overlay, and session save. Audio-only pipeline: system audio → WAV chunks → Dockerized Whisper API → always-on-top overlay. Screen recording is Phase 2 (not yet implemented).

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
cd src/WhisperBurner.WinUI

# First build only — compile the AppxStub (required once per machine/checkout)
dotnet build .tools/AppxStub/AppxStub.csproj -c Release -o .tools/AppxPackage

# Regular build
dotnet build -c Debug

# Run
dotnet run -c Debug
```

> The `.tools/AppxPackage/Microsoft.Build.AppxPackage.dll` stub must exist before the main build. The csproj auto-builds it via a `BeforeBuild` target if absent, but the first explicit build of the stub is faster.

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
- `docker-compose.yml` — `gpu` and `cpu` profiles; mounts `./models` and `./videos`
- `process-videos.ps1` — batch transcription + subtitle burn
- `videos/` — source files; `videos/output/` — SRT + MP4 outputs

### WinUI 3 App (`src/WhisperBurner.WinUI/`)

Built on **Windows App SDK 2.1 unpackaged**, targeting `net10.0-windows10.0.19041.0`. Follows the **WinUI Gallery** code style as the reference implementation.

**Key packages:**
- `Microsoft.WindowsAppSDK` 2.1.3 — WinUI 3 platform
- `CommunityToolkit.WinUI.Controls.SettingsControls` — `SettingsCard`, `SettingsExpander` for settings UI
- `CommunityToolkit.WinUI.Converters` — `BoolToVisibilityConverter`, `StringVisibilityConverter`
- `CommunityToolkit.WinUI.Animations` — animation utilities
- `Microsoft.Windows.CsWin32` — source-generated Win32 P/Invoke (declare API names in `NativeMethods.txt`)
- `NAudio` — audio capture (WasapiLoopbackCapture / WaveInEvent)

**Layer structure:**

| Folder | Purpose |
|---|---|
| `Helpers/` | `ThemeHelper` — dark/light/system theme across all windows; `WindowHelper` — tracks `ActiveWindows`, sets min size |
| `Infrastructure/` | `AppSettings` (JSON, `~/whisper.burner/settings.json`), `AppLogger` (file logger) |
| `Models/` | Immutable record types — `SubtitleSegment`, `CaptureRegion`, `AudioChunkInfo`, `SessionManifest`, `RecordingOptions`, `ApiHealthInfo` |
| `Services/Audio/` | `IRecordingService` / `RecordingService` (NAudio + bounded Channel), `ITranscriptionClient` / `TranscriptionClient` (HTTP multipart), `ISubtitleService` / `SubtitleService` (NDJSON streaming + SRT export) |
| `Services/Video/` | `ISessionRepository` / `SessionRepository` (manifest JSON), `IRegionSelectionService` / `RegionSelectionService` (screen capture) |
| `Styles/` | `Brushes.xaml` — `ThemeDictionaries` (Light/Dark) for `WaveformBarBrush`, `SavedBannerBackgroundBrush`, `LiveTranscriptBackgroundBrush`; also `WaveformBarStyle`, `GhostButtonStyle`, `StopButtonStyle` |
| `Views/` | `RecordingPage`, `SettingsPage` (uses `SettingsExpander`/`SettingsCard`), `SessionReviewPage` (placeholder) |
| `Overlay/` | `SubtitleOverlayWindow` (always-on-top, layered, drag strip), `RegionSelectorWindow` (fullscreen selector), `SubtitleLine` (INotifyPropertyChanged for font-size binding) |

**App shell (WinUI Gallery pattern):**
- `App.xaml` — merges `Brushes.xaml`, declares CommunityToolkit converters in `ThemeDictionaries`
- `App.xaml.cs` — owns all service singletons; calls `WindowHelper.TrackWindow()` + `ThemeHelper.Initialize()` on launch
- `MainWindow.xaml` — `MicaBackdrop` in XAML, `TitleBar` control (`ExtendsContentIntoTitleBar`), `NavigationView` (Record, Sessions) + settings gear
- `MainWindow.xaml.cs` — `WindowHelper.SetWindowMinSize`, `ThemeHelper.IsDarkTheme()` for caption button colour

**Data flow (recording session):**
```
RecordingPage → RecordingService.StartAsync()
  └─ NAudio DataAvailable → FlushChunk() → Channel<AudioChunkInfo>
  └─ ConsumeChunksAsync() → TranscriptionClient.TranscribeChunkAsync()
  └─ SubtitleService.AppendSegments() → SegmentAdded event
  └─ RecordingPage.OnSegmentAdded() → SubtitleOverlayWindow.ShowSegment()
```

**AppxStub (`.tools/AppxStub/`):** Stub `Microsoft.Build.AppxPackage.dll` that satisfies MSBuild task references from `Microsoft.WindowsAppSDK` when the VS AppxPackage workload is absent. Built to `.tools/AppxPackage/`; `AppxMSBuildToolsPath` in the csproj redirects to it.

**Old project:** `old-one/WhisperBurner.WinUI/` — archived prior implementation; reference for business logic only, do not modify.

## Naming Conventions

- Booleans: `is`, `has`, `can`, `should` prefix
- Event handlers: `On` prefix (`OnSegmentAdded`, `OnAudioChunkReady`)
- Async methods: verb prefix (`StartRecordingAsync`, `TranscribeChunkAsync`)
- No single-letter names except `i`/`j` in simple loops

## C# / WinUI 3 Conventions

- PascalCase everywhere; `async`/`await` on all I/O paths; nullable enabled
- **UI thread vs background thread**: `RecordingService`, `SubtitleService`, and `TranscriptionClient` raise events from background threads (audio capture callbacks, HTTP responses, channel consumers). Any event handler in a `Page`/`Window` that touches UI elements (`TextBlock.Text`, `Visibility`, brushes, etc.) must marshal back to the UI thread with `DispatcherQueue.TryEnqueue(() => ...)` — see `CaptionOverlayWindow.ShowSegment`/`ClearLines`/`SetLanguage` and `LiveTranscriptPage.OnStateChanged` for the established pattern. Conversely, never wrap a service's internal logic in `DispatcherQueue.TryEnqueue` — that's a presentation-layer concern, not the service's
- Services are singletons owned by `App`; pages access them via `((App)Application.Current).ServiceName`
- `WindowHelper.TrackWindow()` on every new `Window` — required for `ThemeHelper` to reach all windows
- Custom brushes go in `Styles/Brushes.xaml` under `ThemeDictionaries`, never hardcoded in XAML
- Settings UI uses `SettingsCard` / `SettingsExpander` from `CommunityToolkit.WinUI.Controls`; no raw `Expander` + `StackPanel`
- Win32 P/Invoke: add API name to `NativeMethods.txt` for CsWin32. For complex interop not yet in CsWin32 (window style bits, DWM attributes), `[DllImport]` with explicit constants is acceptable
- `CancellationTokenSource` per recording session; cancel on stop and on page `Unloaded`
- Never implement UI logic directly in a `Page` or `Window` — delegate to a service
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
