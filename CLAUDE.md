# CLAUDE.md

Containerized OpenAI Whisper ASR with GPU/CPU Docker profiles. Batch-processes videos from `videos/` into SRT transcripts and burnt-in MP4s saved to `videos/output/`.

## Architecture

- `Dockerfile` — Python 3.12-slim base, ffmpeg, openai-whisper
- `docker-compose.yml` — `gpu` and `cpu` profiles; mounts `./models` and `./videos`
- `process-videos.ps1` — batch transcription + subtitle burn script
- `process-videos-gpu.cmd` / `process-videos-cpu.cmd` — double-click launchers
- `videos/` — source files (any format); `videos/output/` — SRT + MP4 outputs

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

## Agent Behaviour

- Always use `-LiteralPath` in PowerShell when handling files in `videos/`
- Run `docker compose --profile gpu build --no-cache` when Dockerfile changes
- Never modify files in `videos/output/` — they are generated artifacts
- Skip already-processed files (SRT/MP4 exist checks) before running docker
