# Legacy — whisper-burner (batch subtitle burner)

> **Status: legacy.** This is the project's original workflow: batch-transcribe a
> folder of videos with Whisper and permanently **burn** the subtitles into MP4
> with ffmpeg. It still works and ships in `scripts/batch/`, but the project has
> pivoted to the real-time translation app (**WhisperLive**) — see the main
> [README](../README.MD). This document is kept **separate** so the two are not
> confused: the batch burner is a file-in / file-out CLI pipeline, whereas
> WhisperLive is a live desktop app. They share only the Docker Whisper image.

---

## What it does

Transcribes every video in `videos/` and outputs, to `videos/output/`:

- `filename.srt` — the transcript
- `filename.mp4` — the video with subtitles burned in (no separate subtitle file needed by the player)
- `filename.<lang>.srt` — optional translated subtitles

```
videos/*.{mp4,mkv,...} ──► Whisper (Docker) ──► .srt ──► [translate] ──► ffmpeg burn ──► videos/output/*.mp4
```

## Features

- GPU-accelerated transcription via NVIDIA CUDA, with a CPU fallback
- Any video/audio format — `.wmv`, `.mp4`, `.mkv`, `.avi`, `.mov`, `.webm`, `.ts`, `.mp3`, `.wav`, and more
- Subtitles burned directly into the MP4
- Optional post-transcription translation into any language
- Skips already-processed files — safe to re-run
- Double-click launchers for Windows

## Requirements

- [Docker Desktop](https://www.docker.com/products/docker-desktop/)
- **GPU mode only:** NVIDIA GPU + [NVIDIA Container Toolkit](https://docs.nvidia.com/datacenter/cloud-native/container-toolkit/install-guide.html)

---

## Quick Start

```powershell
# 1. Build the image
docker compose -f docker/docker-compose.yml --profile gpu build   # or --profile cpu

# 2. Drop videos into videos/

# 3. Run
.\scripts\batch\process-videos-gpu.cmd    # or .\scripts\batch\process-videos-cpu.cmd
```

## Translate subtitles

Double-click **`scripts\batch\translate.cmd`** for an interactive translator: it shows a
language menu, prompts for a code, transcribes every video in `videos/`, and translates the
resulting `.srt`.

| File | Description |
|------|-------------|
| `filename.srt` | Original transcript |
| `filename.vi.srt` | Translated subtitles (e.g. Vietnamese) |
| `filename.mp4` | Video with original subtitles burned in |

Full list of codes: [ISO 639-1 language codes](https://en.wikipedia.org/wiki/List_of_ISO_639-1_codes).

## PowerShell options

```powershell
.\scripts\batch\process-videos.ps1 [-Model <model>] [-Language <lang>] [-Task <task>] `
    [-TargetLang <lang>] [-OutputFormat <fmt>] [-Gpu] [-SkipBurn] [-BurnOnly]
```

| Parameter | Default | Description |
|-----------|---------|-------------|
| `-Model` | `turbo` | Whisper model (`turbo`, `large-v3`) |
| `-Language` | auto-detect | Force source language (e.g. `English`, `Japanese`) |
| `-Task` | `transcribe` | `transcribe` keeps the source language; `translate` forces English output |
| `-TargetLang` | — | Translate the SRT into this language after transcription (e.g. `vi`, `fr`) |
| `-OutputFormat` | `srt` | Subtitle format (`srt`, `vtt`, `txt`) |
| `-Gpu` | off | Use the GPU profile |
| `-SkipBurn` | off | Transcribe only, skip the MP4 burn |
| `-BurnOnly` | off | Skip transcription, burn an existing SRT only |

**Examples**

```powershell
# Transcribe with GPU, largest model
.\scripts\batch\process-videos.ps1 -Gpu -Model large-v3

# Force Japanese source, GPU
.\scripts\batch\process-videos.ps1 -Gpu -Language Japanese

# Transcribe and translate SRT to Vietnamese (GPU)
.\scripts\batch\process-videos.ps1 -Gpu -TargetLang vi

# Transcribe only, no burn
.\scripts\batch\process-videos.ps1 -Gpu -SkipBurn

# Burn subtitles into MP4 from an existing SRT, skip re-transcription
.\scripts\batch\process-videos.ps1 -Gpu -BurnOnly
```

## How it works

1. **Transcribe** — Whisper runs inside Docker and generates an `.srt` subtitle file.
2. **Translate** *(optional)* — `docker/translate_srt.py` translates the `.srt` into the target language, saved as `filename.<lang>.srt`.
3. **Burn** — ffmpeg overlays the subtitles onto the video and outputs an `.mp4`.

The GPU is used only during Whisper transcription (the heavy ML step). The ffmpeg burn always runs on CPU — there is no GPU path for subtitle rendering.

---

## Troubleshooting

**`EOFError: marshal data too short` during build** — corrupted Python bytecode in the Docker cache. Rebuild with `docker compose -f docker/docker-compose.yml --profile gpu build --no-cache`.

**WMV corrupt frame warnings** (`[wmv3] concealing DC/AC/MV errors in P frame`) — from the source file, non-fatal; ffmpeg handles them.

**Subtitles not rendering — comma in filename** — the ffmpeg `subtitles=` filter treats commas specially; the script escapes them automatically.

---

## Relationship to WhisperLive

| | Legacy burner (this doc) | WhisperLive (main product) |
|---|---|---|
| Input | Video files in `videos/` | Live system audio (WASAPI loopback) |
| Output | SRT + burned-in MP4 | Live on-screen overlay + saved SRT |
| Interface | CLI / `.cmd` launchers | WinUI 3 desktop app |
| Docker use | Transcribe + ffmpeg burn | `api_server.py` ASR/translate API |
| Code | `scripts/batch/`, `docker/batch_transcribe.py`, `docker/translate_srt.py` | `src/WhisperLive/` |

Do not mix the two flows. Changes to WhisperLive should not touch the batch scripts, and vice versa.
