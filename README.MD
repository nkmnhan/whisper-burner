# whisper-burner — Docker OpenAI Whisper GPU Subtitle Burner

Batch-transcribe videos with **OpenAI Whisper** running in **Docker** (GPU via NVIDIA CUDA or CPU fallback) and permanently burn the subtitles into MP4 using **ffmpeg** — automatic speech recognition for any video format on Windows.

[![Docker](https://img.shields.io/badge/Docker-ready-blue?logo=docker)](https://www.docker.com/)
[![Whisper](https://img.shields.io/badge/OpenAI-Whisper-green?logo=openai)](https://github.com/openai/whisper)
[![GPU](https://img.shields.io/badge/NVIDIA-CUDA-76B900?logo=nvidia)](https://developer.nvidia.com/cuda-toolkit)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

> **Disclaimer:** This is an unofficial, community-maintained Docker wrapper around [OpenAI Whisper](https://github.com/openai/whisper). It is not affiliated with or endorsed by OpenAI. This project is maintained as a personal hobby — use it as-is, no support is guaranteed.

---

## ❤️ Sponsor

If **whisper-burner** saves you time, please consider sponsoring to keep the project maintained and updated.

<p align="center">
  <a href="https://github.com/sponsors/nkmnhan">
    <img src="https://img.shields.io/badge/Sponsor%20on%20GitHub-%E2%9D%A4-red?style=for-the-badge&logo=github" alt="Sponsor on GitHub" />
  </a>
</p>

---

## Features

- GPU-accelerated transcription via NVIDIA CUDA
- CPU fallback mode
- Supports any video/audio format — `.wmv`, `.mp4`, `.mkv`, `.avi`, `.mov`, `.webm`, `.ts`, `.mp3`, `.wav`, and more
- Subtitles burned into MP4 — no separate subtitle file needed by the player
- Translate subtitles into any language after transcription
- Skips already-processed files — safe to re-run
- Double-click launchers for Windows

---

## Requirements

- [Docker Desktop](https://www.docker.com/products/docker-desktop/)
- **GPU mode only**: NVIDIA GPU + [NVIDIA Container Toolkit](https://docs.nvidia.com/datacenter/cloud-native/container-toolkit/install-guide.html)

---

## Quick Start

**1. Clone and build**

```powershell
git clone https://github.com/nkmnhan/whisper-burner.git
cd whisper-burner
docker compose --profile gpu build
```

**2. Drop videos into the `videos/` folder**

**3. Run**

```powershell
# GPU (recommended)
.\process-videos-gpu.cmd

# CPU
.\process-videos-cpu.cmd
```

Outputs saved to `videos/output/`:
- `filename.srt` — transcript
- `filename.mp4` — video with burned-in subtitles

---

## Translate Subtitles

Double-click **`translate.cmd`** to launch an interactive translator.

It displays a language menu, prompts for a code, then transcribes every video in `videos/` and translates the resulting `.srt`:

```
 ==========================================
  Whisper Subtitle Translator
 ==========================================

 Common language codes:

   af  Afrikaans      ar  Arabic          bg  Bulgarian
   ...
   vi  Vietnamese     zh  Chinese         ...

 ==========================================

 Enter language code: vi

 Translating subtitles to: vi
```

Translated SRT files are saved alongside the originals in `videos/output/`:

| File | Description |
|------|-------------|
| `filename.srt` | Original transcript |
| `filename.vi.srt` | Translated subtitles |
| `filename.mp4` | Video with original subtitles burned in |

> Full list of codes: [ISO 639-1 language codes](https://en.wikipedia.org/wiki/List_of_ISO_639-1_codes)

---

## PowerShell Options

```powershell
.\process-videos.ps1 [-Model <model>] [-Language <lang>] [-Task <task>] [-TargetLang <lang>] [-OutputFormat <fmt>] [-Gpu] [-SkipBurn] [-BurnOnly]
```

| Parameter | Default | Description |
|-----------|---------|-------------|
| `-Model` | `turbo` | Whisper model (`turbo`, `large-v3`) |
| `-Language` | auto-detect | Force source language (e.g. `English`, `Japanese`) |
| `-Task` | `transcribe` | `transcribe` keeps original language; `translate` forces English output |
| `-TargetLang` | — | Translate the SRT into this language after transcription (e.g. `vi`, `fr`) |
| `-OutputFormat` | `srt` | Subtitle format (`srt`, `vtt`, `txt`) |
| `-Gpu` | off | Use GPU profile |
| `-SkipBurn` | off | Transcribe only, skip MP4 burn |
| `-BurnOnly` | off | Skip transcription, burn existing SRT only |

**Examples**

```powershell
# Transcribe with GPU, largest model
.\process-videos.ps1 -Gpu -Model large-v3

# Force Japanese source, GPU
.\process-videos.ps1 -Gpu -Language Japanese

# Transcribe and translate SRT to Vietnamese (GPU)
.\process-videos.ps1 -Gpu -TargetLang vi

# Transcribe only, no burn
.\process-videos.ps1 -Gpu -SkipBurn

# Burn subtitles into MP4 from existing SRT, skip re-transcription
.\process-videos.ps1 -Gpu -BurnOnly
```

---

## Whisper Models

| Model | VRAM | Notes |
|-------|------|-------|
| `turbo` | ~8 GB | Default — fast and accurate |
| `large-v3` | 10-15 GB | Highest accuracy |

---

## Project Structure

```
whisper-burner/
├── Dockerfile                  # Python 3.12-slim + ffmpeg + openai-whisper
├── docker-compose.yml          # gpu and cpu profiles
├── process-videos.ps1          # Main batch script
├── process-videos-gpu.cmd      # Double-click launcher (GPU)
├── process-videos-cpu.cmd      # Double-click launcher (CPU)
├── translate.cmd               # Translate subtitles (e.g. translate.cmd vi)
├── translate_srt.py            # SRT translation helper (runs inside container)
├── videos/                     # Drop source videos here
│   └── output/                 # Generated SRT + MP4
└── models/                     # Whisper model cache (auto-populated)
```

---

## How It Works

1. **Transcribe** — Whisper runs inside Docker and generates an `.srt` subtitle file
2. **Translate** *(optional)* — `translate_srt.py` translates the `.srt` into the target language and saves a `filename.<lang>.srt`
3. **Burn** — ffmpeg overlays the subtitles onto the video and outputs an `.mp4`

The GPU is used during Whisper transcription (the heavy ML step). The ffmpeg burn step always runs on CPU — there is no GPU path for subtitle rendering.

---

## Troubleshooting

**`EOFError: marshal data too short` during build**
Corrupted Python bytecode in Docker cache. Rebuild with:
```powershell
docker compose --profile gpu build --no-cache
```

**WMV corrupt frame warnings**
```
[wmv3] concealing DC/AC/MV errors in P frame
```
These come from the source file and are non-fatal. ffmpeg handles them automatically.

**Subtitles not rendering — comma in filename**
The ffmpeg `subtitles=` filter treats commas as special characters. The script escapes them automatically.

---

## References

- [OpenAI Whisper](https://github.com/openai/whisper)
- [NVIDIA Container Toolkit](https://docs.nvidia.com/datacenter/cloud-native/container-toolkit/latest/install-guide.html)
- [Docker Desktop](https://www.docker.com/products/docker-desktop/)

