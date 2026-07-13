# Copilot Instructions — whisper-burner

**Primary product: a real-time speech-translation desktop app.**

1. **WhisperLive WinUI 3 app** (`src/WhisperLive/`) — real-time system-audio capture → transcription → live translation → always-on-top subtitle overlay. This is the focus.
2. **Docker ASR/translate API backend** (`docker/api_server.py`) — FastAPI + faster-whisper serving `/transcribe` and `/translate` that the app calls.
3. **Legacy batch pipeline** (`scripts/batch/`) — the original Whisper ASR + ffmpeg subtitle-burn workflow. Still works, but the project pivoted (2026-07-13) away from it toward the real-time app; treat it as legacy. Kept and documented for reuse in [`docs/legacy-whisper-burner.md`](../docs/legacy-whisper-burner.md) — keep it isolated from `src/WhisperLive/`.

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
.\scripts\app\build-release.cmd
# outputs to release\ with WindowsAppSdkSelfContained=true
```

Run `dotnet build` after **every** C# change before reporting done.

### Docker — Whisper API backend (used by the app)

```powershell
# Interactive (recommended): whisper.cmd → [6] Manage API → CPU/GPU, model, Start/Reset
docker compose -f docker/docker-compose.yml --profile cpu up -d    # or --profile gpu
```

API runs at `http://127.0.0.1:5000`. Endpoints: `GET /health`, `GET /models`, `POST /transcribe`, `POST /translate`.
`docker/api_server.py` is **volume-mounted** — restart/Reset the container to pick up edits; only rebuild (`--build`) after `Dockerfile`/dependency changes.

### Docker — Batch transcription (legacy)

```powershell
docker compose -f docker/docker-compose.yml --profile gpu build
.\scripts\batch\process-videos-gpu.cmd   # or -cpu
```

---

## Architecture

### Docker backend

`docker/api_server.py` (FastAPI + faster-whisper + deep-translator) is the app's ASR/translate backend, served by uvicorn on port 5000 via the `gpu` / `cpu` compose profiles. Key env: `WHISPER_MODEL`, `NUM_WORKERS`, `CPU_THREADS`, `LOG_RESULT`. The file is volume-mounted, so edits apply on container restart.

**Legacy batch:** `process-videos.ps1` orchestrates transcribe → optional `translate_srt.py` → ffmpeg subtitle burn, output to `videos/output/` (never modify that directory). No longer the project focus — full reference in [`docs/legacy-whisper-burner.md`](../docs/legacy-whisper-burner.md).

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

## Quick-Reference Alias Glossary

When the user refers to a component by shorthand, map it immediately to the correct file(s) and design reference.

**WinUI Gallery** — Local: `C:\nkmn\Projects\WinUI-Gallery\WinUIGallery\Samples\<Name>` · GitHub: `https://github.com/microsoft/WinUI-Gallery/tree/main/WinUIGallery/Samples/<Name>`

---

### 🔵 Special Alias: `msgallery`

**`msgallery`** is a validation trigger. When the user writes it (e.g. "check msgallery", "validate msgallery", "fix this to follow msgallery"):

1. **Identify** which WinUI Gallery sample(s) are relevant to the component being discussed (use the category map in `.claude/skills/winui-gallery-design/SKILL.md`)
2. **Read** `C:\nkmn\Projects\WinUI-Gallery\WinUIGallery\Samples\<RelevantName>\<RelevantName>Page.xaml` (and `.xaml.cs`)
3. **Audit** the existing WhisperBurner code against:
   - Control composition (is the right control used?)
   - Resource usage (`{ThemeResource ...}` — never hardcoded colours/sizes)
   - Spacing and padding (follow the gallery's numeric values)
   - Typography (use `{StaticResource BodyTextBlockStyle}` etc., never inline `FontSize`)
   - Accessibility (`AutomationProperties`, keyboard nav, contrast)
   - Animation/motion (use gallery storyboard patterns, not ad-hoc)
4. **Report** specific deviations as a list — what's wrong → what the gallery pattern is → which file/line to fix
5. **Fix** them unless the user said only to audit

If `msgallery` is written without specifying a component, audit **`Views/LiveTranscriptPage.xaml`** as the default scope.

---

### UI — Pages & Windows

| Alias | Code file(s) | WinUI Gallery ref |
|---|---|---|
| **live page**, transcript page, main page | `Views/LiveTranscriptPage.xaml` + `.cs` | — |
| **sessions page**, history page | `Views/SessionsPage.xaml` + `.cs` | — |
| **settings page** | `Views/SettingsPage.xaml` + `.cs` | CommunityToolkit `SettingsCards` |
| **overlay**, caption box, subtitle overlay | `Components/CaptionOverlayWindow.xaml` + `.cs` | `Windowing/AppWindow`, `Styles/SystemBackdrops` |
| **shell**, nav shell, main window | `MainWindow.xaml` + `.cs` | `Navigation/NavigationView`, `Windowing/TitleBar` |
| **app entry**, service wiring, singletons | `App.xaml.cs` | — |

### UI — Controls & Elements (all in `LiveTranscriptPage` unless noted)

| Alias | Code / Element | WinUI Gallery ref |
|---|---|---|
| **chips**, skill chips, suggestion chips, seeding chips | `SuggestionChipsPanel` buttons + `OnSuggestionClicked`; style `SuggestionChipButtonStyle` → `Styles/Brushes.xaml`; model `Models/SessionSkill.cs` | `BasicInput/Button` |
| **chat list**, assistant chat, chat bubbles, message list | `AssistantChatList` ListView; template `Helpers/AssistantMessageTemplateSelector.cs`; model `Models/AssistantMessage.cs` | `Collections/ListView` |
| **ask box**, question box, assistant input | `AssistantQuestionBox` AutoSuggestBox | `Text/AutoSuggestBox` |
| **assistant panel**, ai panel | `AssistantPanel` Border | — |
| **transcript list**, segment list | `TranscriptList` ListView; data `ViewModels/TranscriptViewModel.cs` | `Collections/ListView` |
| **thinking indicator**, spinner, loading | `ThinkingIndicator` + `ShowThinkingStoryboard`/`HideThinkingStoryboard` | `StatusAndInfo/ProgressRing` |
| **pre-context box**, meeting notes box, agenda box | `PreContextBox` TextBox | `Text/TextBox` |
| **context history**, recent prompts flyout | `OnContextHistoryClicked` → `MenuFlyout` | `DialogsAndFlyouts/Flyout` |
| **translation chip**, language chip | `TranslationChip` Border | — |
| **record button**, start/stop button | `RecordButton` | `BasicInput/Button` |
| **ghost button** | `Components/GhostButton.cs`; style `GhostButtonStyle` → `Styles/Brushes.xaml` | `BasicInput/Button` transparent |
| **brushes**, styles, theme colors | `Styles/Brushes.xaml` | `Design/Color`, `Fundamentals/XamlStyles` |

### Services

| Alias | File(s) |
|---|---|
| **srt writer**, streaming srt, srt writing | `Services/Audio/StreamingSrtWriter.cs` + `ISrtSessionWriter.cs` |
| **recording service**, audio capture, loopback, wasapi | `Services/Audio/RecordingService.cs` + `IRecordingService.cs` |
| **recording manager**, recorder, orchestrator | `Services/Audio/RecordingManager.cs` + `IRecordingManager.cs` |
| **transcription client**, whisper client | `Services/Audio/TranscriptionClient.cs` + `ITranscriptionClient.cs` |
| **subtitle service**, segment store | `Services/Audio/SubtitleService.cs` + `ISubtitleService.cs` |
| **translation service**, translator | `Services/Translation/TranslationService.cs` + `ITranslationService.cs` |
| **translation provider**, deepl, google, docker translate | `Services/Translation/Providers/` — `DeepL`, `Google`, `Docker`, `PassThrough` providers |
| **assistant service**, ai service, session assistant | `Services/Assistant/SessionAssistantService.cs` + `ISessionAssistantService.cs` |
| **claude provider**, claude cli | `Services/Assistant/ClaudeCliProvider.cs` + `IAiProvider.cs` |
| **ai session**, claude session | `Services/Assistant/IAiSession.cs` (impl inside `ClaudeCliProvider.cs`) |
| **export service**, assistant export | `Services/Assistant/AssistantExportService.cs` + `IAssistantExportService.cs` |

### Models & Infrastructure

| Alias | File |
|---|---|
| **segment**, subtitle segment | `Models/SubtitleSegment.cs` |
| **translated segment** | `Models/TranslatedSegmentView.cs` |
| **skill**, session skill | `Models/SessionSkill.cs` |
| **assistant message**, chat message | `Models/AssistantMessage.cs` |
| **corrected segment** | `Models/CorrectedSegment.cs` |
| **notes bubble**, session notes | `Models/NotesBubble.cs`, `Models/SessionNotes.cs` |
| **saved prompt**, context preset | `Models/SavedPrompt.cs` |
| **settings**, app settings, config | `Infrastructure/AppSettings.cs` → `~/whisper.live/settings.json` |
| **logger**, logging | `Infrastructure/AppLogger.cs` |
| **theme helper** | `Helpers/ThemeHelper.cs` |
| **window helper** | `Helpers/WindowHelper.cs` |
| **message template selector** | `Helpers/AssistantMessageTemplateSelector.cs` |
| **markdown helper** | `Helpers/MarkdownHelper.cs` |
| **view model**, transcript vm | `ViewModels/TranscriptViewModel.cs` |

### Runtime Context Files (`~/whisper.live/`)

| Alias | Path | Managed by |
|---|---|---|
| **global claude md**, global ai instructions | `~/whisper.live/CLAUDE.md` | User edits in Settings page |
| **session claude md**, per-session context | `~/whisper.live/session-active/CLAUDE.md` | `SessionAssistantService.StartSession()` / `EndSession()` |

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
