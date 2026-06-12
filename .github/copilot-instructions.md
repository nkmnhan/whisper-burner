# Copilot Instructions — whisper-burner

Two independent components share this repository:

1. **Docker batch pipeline** — Whisper ASR + ffmpeg subtitle burning
2. **WhisperLive WinUI 3 app** (`src/WhisperLive/`) — real-time system-audio capture → live subtitle overlay

---

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
# outputs to release\ with WindowsAppSdkSelfContained=true
```

Run `dotnet build` after **every** C# change before reporting done.

### Docker — Batch transcription

```powershell
# GPU
docker compose --profile gpu build
.\process-videos-gpu.cmd

# CPU
docker compose --profile cpu build
.\process-videos-cpu.cmd
```

### Docker — Whisper API server (used by WhisperLive app)

```powershell
.\start-api-gpu.cmd   # docker compose --profile api-gpu up --build
.\start-api-cpu.cmd   # docker compose --profile api-cpu up --build
```

API runs at `http://localhost:5000`. Endpoints: `GET /health`, `GET /models`, `POST /transcribe`.

Rebuild with `--no-cache` after any `Dockerfile` change:
```powershell
docker compose --profile gpu build --no-cache
```

---

## Architecture

### Docker pipeline

`process-videos.ps1` orchestrates: transcribe via `docker compose run` → optional `translate_srt.py` inside container → ffmpeg subtitle burn. Outputs go to `videos/output/` only — never modify this directory.

Four Docker profiles: `gpu`, `cpu`, `api-gpu`, `api-cpu`. The `api-*` profiles run `api_server.py` as a FastAPI/uvicorn service on port 5000.

### WinUI 3 App (`src/WhisperLive/`)

**Target:** `net9.0-windows10.0.22621.0`, unpackaged (`WindowsPackageType=None`), x64 only.

**App shell pattern (WinUI Gallery style):**
- `App.xaml.cs` — owns all service singletons; stores `_currentSettings` in memory, updates it via `ApplySettings()`, constructs `SessionAssistantService(RecordingManager, aiProvider, () => _currentSettings)`, and creates `CaptionOverlayWindow`
- `MainWindow` — `MicaBackdrop`, `TitleBar` (`ExtendsContentIntoTitleBar`), `NavigationView` → `LiveTranscriptPage` / `SessionsPage` / `SettingsPage`

**Data flow:**
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
  └─ LiveTranscriptPage binds to TranscriptViewModel.Segments (ObservableCollection)
  └─ CaptionOverlayWindow.ShowSegment() ← called from LiveTranscriptPage via DispatcherQueue
```

**Layer map:**

| Folder | Key types |
|---|---|
| `App.xaml` / `App.xaml.cs` | service singletons, `_currentSettings`, `ApplySettings()`, `CaptionOverlayWindow`, `SessionAssistantService(...)` |
| `MainWindow.xaml` / `.cs` | `MicaBackdrop`, `TitleBar`, `NavigationView` shell |
| `Components/` | `CaptionOverlayWindow`, `GhostButton` |
| `Helpers/` | `ThemeHelper`, `TitleBarHelper`, `WindowHelper`, `AssistantMessageTemplateSelector`, `MarkdownHelper` |
| `Infrastructure/` | `AppSettings` (JSON, `~/whisper.live/settings.json`), `AppLogger` (Serilog wrapper) |
| `Models/` | `AssistantMessage`, `AudioChunkInfo`, `CorrectedSegment`, `NotesBubble`, `RecordingOptions`, `RecordingState`, `SavedPrompt`, `SessionNotes`, `SessionSkill`, `SubtitleSegment`, `TranscriptDisplayMode`, `TranslatedSegmentView`, `TranslationSegmentState` |
| `Services/Audio/` | `ISrtSessionWriter`, `StreamingSrtWriter`, `IRecordingService` / `RecordingService`, `ISubtitleService` / `SubtitleService`, `ITranscriptionClient` / `TranscriptionClient`, `IRecordingManager` / `RecordingManager` |
| `Services/Translation/` | `ITranslationService`, `TranslationService`, `DisabledTranslationService`, `SegmentTranslationReadyEventArgs`, translation providers |
| `Services/Assistant/` | `ISessionAssistantService`, `SessionAssistantService`, `IAiProvider`, `ClaudeCliProvider`, `IAiSession`, `IAssistantExportService`, `AssistantExportService`, `AiCallContext`, `AiStreamChunk`, `AskOptions` |
| `Styles/` | `Brushes.xaml` — `ThemeDictionaries`, `GhostButtonStyle`, `InlineActionButtonStyle`, `SuggestionChipButtonStyle` |
| `ViewModels/` | `TranscriptViewModel` — `ObservableCollection<TranslatedSegmentView>`, `FinalizeSession()` |
| `Views/` | `LiveTranscriptPage`, `SessionsPage`, `SettingsPage` |

---

## Key Conventions

### C# / WinUI 3

- Services are singletons on `App`; pages access them via `((App)Application.Current).ServiceName`
- **Thread safety**: `RecordingService`, `SubtitleService`, `TranscriptionClient`, and `TranslationService` raise events from background threads. Any `Page`/`Window` event handler that touches UI elements must marshal to the UI thread: `DispatcherQueue.TryEnqueue(() => { ... })`. Never put `DispatcherQueue.TryEnqueue` inside a service — that's a presentation-layer concern
- **Logging**: Use `AppLogger` (Serilog wrapper in `Infrastructure/`) — `AppLogger.Info(...)`, `AppLogger.Warning(...)`, `AppLogger.Error(ex, ...)`. Do not use `Debug.WriteLine` in production paths
- `ISrtSessionWriter` is the shared abstraction for all SRT streaming writes. `SubtitleService` (raw) and `TranslationService` (translated) both use `StreamingSrtWriter` through this interface — never create a raw `StreamWriter` for SRT output
- `SessionAssistantService` receives `Func<AppSettings>` via constructor injection from `App`. Never call `AppSettings.LoadAsync()` inside assistant service methods — use the injected getter
- Segoe MDL2 Assets glyphs in C# must always use `"\uXXXX"` escape sequences — never paste raw Unicode characters. Verified codepoints: `\uE768` Play, `\uE769` Pause, `\uE70D` ChevronDown, `\uE70E` ChevronUp, `\uE711` Close
- `SessionsPage` reads SRT files with `FileShare.ReadWrite` so live-written session files can be opened without lock errors
- `WindowHelper.TrackWindow(this)` in **every** `Window` constructor — required for `ThemeHelper` to apply theme to all windows
- Custom brushes in `Styles/Brushes.xaml` under `ThemeDictionaries` — never hardcoded in XAML
- Settings UI: `SettingsCard` / `SettingsExpander` from `CommunityToolkit.WinUI.Controls` — not raw `Expander` + `StackPanel`
- Win32 P/Invoke: add API name to `NativeMethods.txt` (CsWin32 source-gen). Use `[DllImport]` only for things CsWin32 doesn't cover (e.g. DWM attributes, window style bits)
- `CancellationTokenSource` per recording session; cancel on stop and on page `Unloaded`
- One type per file; interface + model files stay under ~60 lines
- No UI logic in `Page`/`Window` — delegate to a service or view model

### Naming

- Booleans: `is`, `has`, `can`, `should` prefix
- Event handlers: `On` prefix (`OnSegmentAdded`)
- Async methods: verb prefix (`StartAsync`, `TranscribeChunkAsync`)

### PowerShell

- PascalCase functions, camelCase locals; full cmdlet names (no aliases)
- `-LiteralPath` for all `videos/` file operations (handles spaces and commas)

### Docker

- Pin base image tags; one logical step per `RUN`; `--no-install-recommends` on apt
- `videos/output/` is generated — never modify programmatically
- Skip already-processed files (check SRT/MP4 exist before running docker)

---

## Whisper Models

| Model | VRAM | Notes |
|---|---|---|
| `turbo` | ~8 GB | Default |
| `large-v3` | 10–15 GB | Highest accuracy |

API server default is controlled by `WHISPER_MODEL` env var (defaults to `small` in `docker-compose.yml`).

---

## Important Constraints

- **NEVER** overwrite source files in `videos/` or session recordings — immutable
- Screen recording / video capture is **not yet implemented** — do not add it without user approval
- `old-one/` directory is an archived prior implementation — reference only, do not modify
