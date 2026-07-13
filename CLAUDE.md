# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

**Primary product: a real-time speech-translation desktop app.** The **WinUI 3 app** (`src/WhisperLive/`) captures system audio, transcribes it via the Whisper API, translates each line live, and shows both in an always-on-top subtitle overlay; every session is saved as SRT. Audio-only pipeline: system audio → Whisper API → translation → overlay. Screen recording is Phase 2 (not yet implemented).

The **Docker** side (`docker/`) is the app's **ASR + translate API backend** — `api_server.py` (FastAPI + faster-whisper) exposing `/transcribe` and `/translate`, run via GPU/CPU compose profiles.

> **Plan note (2026-07-13):** the project pivoted from a Docker *batch convert-and-burn* pipeline (transcribe a folder of videos and burn subtitles into MP4) to the real-time translation app above. The batch scripts in `scripts/batch/` remain as **legacy** but are no longer the focus.

---

## Quick-Reference Alias Glossary

When the user says one of these words, map it immediately to the correct file(s) and design reference.

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
4. **Report** specific deviations as a list, each with: what's wrong → what the gallery pattern is → which file/line to fix
5. **Fix** them unless the user said only to audit

If the user says `msgallery` without specifying a component, audit the **whole `Views/LiveTranscriptPage.xaml`** as the default scope.

---

### UI — Pages & Windows

| Alias | Code file(s) | WinUI Gallery ref |
|---|---|---|
| **live page**, transcript page, main page, recording page | `Views/LiveTranscriptPage.xaml` + `.cs` | — |
| **sessions page**, history page, saved sessions | `Views/SessionsPage.xaml` + `.cs` | — |
| **settings page**, config page | `Views/SettingsPage.xaml` + `.cs` | CommunityToolkit `SettingsCards` |
| **overlay**, caption box, subtitle overlay, always-on-top box | `Components/CaptionOverlayWindow.xaml` + `.cs` | `Windowing/AppWindow`, `Styles/SystemBackdrops` |
| **shell**, nav shell, main window | `MainWindow.xaml` + `.cs` | `Navigation/NavigationView`, `Windowing/TitleBar` |
| **app entry**, service wiring, singletons | `App.xaml.cs` | — |

### UI — Controls & Elements (all in `LiveTranscriptPage` unless noted)

| Alias | Code / Element | WinUI Gallery ref |
|---|---|---|
| **chips**, skill chips, suggestion chips, seeding chips | `SuggestionChipsPanel` buttons + `OnSuggestionClicked`; style `SuggestionChipButtonStyle` → `Styles/Brushes.xaml`; model `Models/SessionSkill.cs` | `BasicInput/Button` |
| **chat list**, assistant chat, chat bubbles, message list | `AssistantChatList` ListView; template `Helpers/AssistantMessageTemplateSelector.cs`; model `Models/AssistantMessage.cs` | `Collections/ListView` |
| **ask box**, question box, assistant input, prompt box | `AssistantQuestionBox` AutoSuggestBox | `Text/AutoSuggestBox` |
| **assistant panel**, ai panel | `AssistantPanel` Border | — |
| **transcript list**, segment list, subtitle list | `TranscriptList` ListView; data `ViewModels/TranscriptViewModel.cs` | `Collections/ListView` |
| **thinking indicator**, spinner, loading | `ThinkingIndicator` + `ShowThinkingStoryboard`/`HideThinkingStoryboard` | `StatusAndInfo/ProgressRing` |
| **pre-context box**, meeting notes box, agenda box, session context box | `PreContextBox` TextBox | `Text/TextBox` |
| **context history**, recent prompts flyout | `OnContextHistoryClicked` → `MenuFlyout` | `DialogsAndFlyouts/Flyout` |
| **translation chip**, language chip | `TranslationChip` Border | — |
| **record button**, start/stop button | `RecordButton` | `BasicInput/Button` |
| **ghost button** | `Components/GhostButton.cs`; style `GhostButtonStyle` → `Styles/Brushes.xaml` | `BasicInput/Button` transparent variant |
| **brushes**, styles, theme colors, button styles | `Styles/Brushes.xaml` | `Design/Color` (`brushes.json`), `Fundamentals/XamlStyles` |

### Services

| Alias | File(s) |
|---|---|
| **srt writer**, streaming srt, srt writing | `Services/Audio/StreamingSrtWriter.cs` + `ISrtSessionWriter.cs` |
| **recording service**, audio capture, loopback, wasapi | `Services/Audio/RecordingService.cs` + `IRecordingService.cs` |
| **recording manager**, recorder, orchestrator | `Services/Audio/RecordingManager.cs` + `IRecordingManager.cs` |
| **transcription client**, whisper client, api client | `Services/Audio/TranscriptionClient.cs` + `ITranscriptionClient.cs` |
| **subtitle service**, segment store, subtitle buffer | `Services/Audio/SubtitleService.cs` + `ISubtitleService.cs` |
| **translation service**, translator | `Services/Translation/TranslationService.cs` + `ITranslationService.cs` |
| **disabled translation** | `Services/Translation/DisabledTranslationService.cs` |
| **translation provider**, deepl, google translate, docker translate | `Services/Translation/Providers/` — `DeepL`, `Google`, `Docker`, `PassThrough` providers |
| **assistant service**, ai service, session assistant | `Services/Assistant/SessionAssistantService.cs` + `ISessionAssistantService.cs` |
| **claude provider**, claude cli, ai backend | `Services/Assistant/ClaudeCliProvider.cs` + `IAiProvider.cs` |
| **ai session**, claude session | `Services/Assistant/IAiSession.cs` (impl inside `ClaudeCliProvider.cs`) |
| **export service**, assistant export | `Services/Assistant/AssistantExportService.cs` + `IAssistantExportService.cs` |

### Models

| Alias | File |
|---|---|
| **segment**, subtitle segment | `Models/SubtitleSegment.cs` |
| **translated segment** | `Models/TranslatedSegmentView.cs` |
| **skill**, session skill | `Models/SessionSkill.cs` |
| **assistant message**, chat message | `Models/AssistantMessage.cs` |
| **corrected segment**, ai correction | `Models/CorrectedSegment.cs` |
| **notes bubble**, session notes | `Models/NotesBubble.cs`, `Models/SessionNotes.cs` |
| **saved prompt**, context preset | `Models/SavedPrompt.cs` |
| **recording options** | `Models/RecordingOptions.cs` |
| **recording state** | `Models/RecordingState.cs` |
| **audio chunk** | `Models/AudioChunkInfo.cs` |
| **transcript display mode** | `Models/TranscriptDisplayMode.cs` |
| **translation state** | `Models/TranslationSegmentState.cs` |

### Infrastructure & Helpers

| Alias | File |
|---|---|
| **settings**, app settings, config | `Infrastructure/AppSettings.cs` → `~/whisper.live/settings.json` |
| **logger**, logging | `Infrastructure/AppLogger.cs` |
| **theme helper** | `Helpers/ThemeHelper.cs` |
| **window helper** | `Helpers/WindowHelper.cs` |
| **title bar helper** | `Helpers/TitleBarHelper.cs` |
| **message template selector**, chat template | `Helpers/AssistantMessageTemplateSelector.cs` |
| **markdown helper** | `Helpers/MarkdownHelper.cs` |
| **view model**, transcript vm | `ViewModels/TranscriptViewModel.cs` |

### Runtime Context Files (`~/whisper.live/`)

| Alias | Path | Managed by |
|---|---|---|
| **global claude md**, global ai instructions, assistant persona | `~/whisper.live/CLAUDE.md` | User edits in Settings page; default from `ClaudeCliProvider.EnsureGlobalClaudeMdAsync()` |
| **session claude md**, per-session context | `~/whisper.live/session-active/CLAUDE.md` | `SessionAssistantService.StartSession()` writes, `EndSession()` deletes |

---

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
.\scripts\app\build-release.cmd
```

### Docker — ASR/translate API backend (used by the app)

```powershell
# Start via the interactive menu (recommended):
.\whisper.cmd   # [6] Manage API → CPU/GPU, model, Start/Reset

# Or directly (api_server.py is volume-mounted — Reset picks up edits, no rebuild):
docker compose -f docker/docker-compose.yml --profile cpu up -d   # or --profile gpu
```

### Docker / Batch transcription (legacy)

```powershell
# Legacy convert-and-burn pipeline — no longer the focus.
docker compose -f docker/docker-compose.yml --profile gpu build
.\scripts\batch\process-videos-gpu.cmd   # or -cpu
```

## Architecture

### Docker backend (`docker/`)

- `Dockerfile` — `python:3.12-slim`, ffmpeg, **faster-whisper**, deep-translator, FastAPI/uvicorn
- `docker-compose.yml` — `gpu` and `cpu` profiles running `api_server.py` on port 5000; env: `WHISPER_MODEL`, `NUM_WORKERS`, `CPU_THREADS`, `LOG_RESULT`. `api_server.py` is volume-mounted (edit → Reset, no rebuild)
- `api_server.py` — the app's backend: `GET /health`, `GET /models`, `POST /transcribe`, `POST /translate`
- **Legacy batch:** `batch_transcribe.py` / `translate_srt.py` + `scripts/batch/process-videos.ps1` — transcribe `videos/` and burn subtitles into MP4 in `videos/output/` (still works; not the focus)

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
