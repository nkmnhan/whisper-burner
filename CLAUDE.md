# CLAUDE.md

Containerized OpenAI Whisper ASR with GPU/CPU Docker profiles. Batch-processes videos from `videos/` into SRT transcripts and burnt-in MP4s saved to `videos/output/`.

Also contains a **WinUI 3 desktop app** (`src/WhisperBurner.WinUI/`) for system-audio capture, live subtitle overlay, and session save. Audio-only pipeline: system audio → WAV chunks → Dockerized Whisper API → always-on-top overlay. Screen recording is Phase 1 (not yet implemented).

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

## Naming Conventions

- **Meaningful names only** — never `d`, `e`, `v`, `tmp`, `res`, `cb`, `fn`, `arr` (except `i`/`j` in simple loops)
- Booleans: `is`, `has`, `can`, `should` prefix
- Event handlers: `On` prefix in C# (`OnAudioChunkReady`), `on`/`handle` in scripts
- Async methods: verb prefix (`StartRecordingAsync`, `TranscribeChunkAsync`)

## Architecture

- `Dockerfile` — Python 3.12-slim base, ffmpeg, openai-whisper
- `docker-compose.yml` — `gpu` and `cpu` profiles; mounts `./models` and `./videos`
- `process-videos.ps1` — batch transcription + subtitle burn script
- `process-videos-gpu.cmd` / `process-videos-cpu.cmd` — double-click launchers
- `videos/` — source files (any format); `videos/output/` — SRT + MP4 outputs
- `src/WhisperBurner.WinUI/` — WinUI 3 desktop app (C# / Windows App SDK 2.1 unpackaged)
  - `Models/` — `CaptureRegion`, `RecordingOptions`, `SubtitleSegment`, `SessionManifest`, `AudioChunkInfo`
  - `Services/Audio/` — `IRecordingService`, `ITranscriptionClient`, `ISubtitleService` + implementations
  - `Services/Video/` — `IRegionSelectionService`, `ISessionRepository` + implementations
  - `Views/` — `RecordingPage`, `SessionReviewPage`, `SettingsPage`
  - `Overlay/` — `SubtitleOverlayWindow`, `RegionSelectorWindow`
  - `Infrastructure/` — `AppSettings`, `AppLogger`
  - `Docs/` — `ARCHITECTURE.md`, `API_CONTRACT.md`, `DECISION_MATRIX.md`, `IMPLEMENTATION_PHASES.md`

## Quick Start

```powershell
# GPU (default)
docker compose --profile gpu build
.\process-videos-gpu.cmd

# CPU
docker compose --profile cpu build
.\process-videos-cpu.cmd
```

## Whisper Models

| Model | VRAM | Notes |
|-------|------|-------|
| `turbo` | ~8 GB | Default — fast, accurate |
| `large-v3` | 10-15 GB | Most accurate |

## Development Workflow

For non-trivial work, follow this sequence:

1. **Design** — explore requirements and constraints before coding
2. **Plan** — create step-by-step implementation plan
3. **Isolate** — use a feature branch or git worktree
4. **Execute** — implement in small, verifiable steps
5. **Verify** — run `dotnet build` (C#) or test the feature manually
6. **Finish** — merge or create PR

## Coding Conventions

### General
- Max 100 lines per file — split into focused single-responsibility files if exceeded
- No comments unless the WHY is non-obvious
- No speculative features — implement only what is asked

### Docker
- Pin base image to a specific version tag (e.g. `python:3.12-slim`)
- One `RUN` layer per logical step; chain with `&&` to minimize layers
- Always `--no-install-recommends` for apt installs
- Never run as root — add a non-root user for production images
- Use `.dockerignore` to exclude build artifacts and secrets
- Multi-stage builds when final image doesn't need build tools

### PowerShell
- PascalCase for functions (`Invoke-Whisper`), camelCase for local variables (`$baseName`)
- Full cmdlet names in scripts — no aliases (`Get-ChildItem` not `ls`)
- Always use `-LiteralPath` when paths may contain special characters (spaces, commas)
- `[string[]]` type hints on function parameters
- Group related logic into small focused functions

### Python (if added)
- Follow PEP 8; 4-space indent
- Type hints on all function signatures
- No bare `except` — catch specific exceptions

### Whisper
- Default model: `turbo` — change only if accuracy is insufficient
- `--output_dir /app/output` keeps outputs separate from source files
- ffmpeg subtitle burn: escape `,` and `:` in filenames for `-vf subtitles=`
- WMV corrupt frame warnings are non-fatal — use `-fflags +discardcorrupt -err_detect ignore_err`

### C# / WinUI 3
- Target framework: `net10.0-windows10.0.19041.0`; Windows App SDK 2.1 unpackaged
- PascalCase everywhere; `async`/`await` on all I/O paths; nullable enabled
- Keep interface and model files under 60 lines — one type per file
- Services are singletons owned by `App`; pages and windows consume them via `(App)Application.Current`
- Use `Func<T, Task>` callbacks instead of `event EventHandler<T>` when the caller must await the handler
- Add a `CancellationTokenSource` per recording session; cancel on stop and on page unload
- Never implement directly in a `Page` or `Window` — delegate to a service
- Original session `recording.mp4` must never be overwritten

## Agent Behaviour

- Always use `-LiteralPath` in PowerShell when handling files in `videos/`
- Run `docker compose --profile gpu build --no-cache` when Dockerfile changes
- Never modify files in `videos/output/` — they are generated artifacts
- Skip already-processed files (SRT/MP4 exist checks) before running docker
- Never implement WinUI 3 features beyond the current phase without user approval

## CRITICAL — verify before every change

1. **NEVER** hardcode secrets or API keys in source files
2. **NEVER** overwrite `recording.mp4` — session source files are immutable
3. **ALWAYS** read existing code before modifying — evidence over assumption
4. **ALWAYS** run `dotnet build` after C# changes before reporting done
