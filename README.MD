# whisper-burner — Real-time Speech Translation for Windows

**WhisperLive** is a Windows desktop app that captures your system audio, transcribes it with **OpenAI Whisper**, translates it live, and shows the result in an always-on-top subtitle overlay — for meetings, videos, streams, and calls in any language.

[![Windows](https://img.shields.io/badge/Windows-WinUI%203-0078D6?logo=windows)](https://learn.microsoft.com/windows/apps/winui/winui3/)
[![.NET](https://img.shields.io/badge/.NET-9-512BD4?logo=dotnet)](https://dotnet.microsoft.com/)
[![Whisper](https://img.shields.io/badge/OpenAI-Whisper-green?logo=openai)](https://github.com/openai/whisper)
[![Docker](https://img.shields.io/badge/Docker-API%20backend-blue?logo=docker)](https://www.docker.com/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

> **Disclaimer:** Unofficial, community-maintained. Built on [OpenAI Whisper](https://github.com/openai/whisper) (via [faster-whisper](https://github.com/SYSTRAN/faster-whisper)); not affiliated with or endorsed by OpenAI. Maintained as a personal hobby — use as-is, no support guaranteed.

---

## ❤️ Sponsor

If **whisper-burner** saves you time, please consider sponsoring to keep it maintained.

<p align="center">
  <a href="https://github.com/sponsors/nkmnhan">
    <img src="https://img.shields.io/badge/Sponsor%20on%20GitHub-%E2%9D%A4-red?style=for-the-badge&logo=github" alt="Sponsor on GitHub" />
  </a>
</p>

---

## What it does

```
System audio ──► Whisper API ──► live transcript ──► translation ──► always-on-top overlay
 (WASAPI loopback)  (Docker)        (SRT stream)      (DeepL/Google/free)   (draggable caption box)
```

WhisperLive listens to whatever is playing on your PC, transcribes it in near real-time, optionally translates each line, and renders both the original and the translation in a floating caption box you can place over any window. Every session is saved as SRT.

## Features

- **Real-time captions** from system audio (WASAPI loopback capture — no microphone needed)
- **Live translation** into any language — DeepL, Google Translate, or the free Docker/deep-translator backend
- **Always-on-top overlay** — draggable, auto-sizing caption box with pause and language chips
- **Bilingual display** — original text + translation, side by side
- **Built-in Claude assistant** — ask questions about the live session transcript
- **Session save** — raw, translated, and final SRT written to `~/whisper.live/sessions/`
- Runs unpackaged, self-contained, no admin required

---

## Requirements

- Windows 10 build 22621 (22H2) or later
- .NET 9 Desktop Runtime
- [Docker Desktop](https://www.docker.com/products/docker-desktop/) — runs the Whisper ASR/translate API
- **GPU (optional, recommended):** NVIDIA GPU + [NVIDIA Container Toolkit](https://docs.nvidia.com/datacenter/cloud-native/container-toolkit/install-guide.html) for faster-than-real-time transcription

---

## Quick Start

Everything is driven from the interactive menu:

```powershell
git clone https://github.com/nkmnhan/whisper-burner.git
cd whisper-burner
.\whisper.cmd
```

**1. Start the Whisper API backend** → menu **[6] Manage API** → choose CPU/GPU, model, then **Start**.
The API runs at `http://127.0.0.1:5000` (`GET /health`, `POST /transcribe`, `POST /translate`).

**2. Build & run the app** → menu **[3] Run app (debug)** or **[4] Build release**.

> On CPU, use the `small` model for near-real-time captions. `medium`/`large-v3` need a GPU to keep up.

---

## Configuration

App settings live at `~/whisper.live/settings.json` (edit in the **Settings** page):

| Setting | Notes |
|---|---|
| `Model` | `tiny` / `base` / `small` / `medium` / `large-v3` / `turbo`. Default `small`. |
| `ChunkDurationSeconds` | Audio chunk length sent per request. Default `5`. |
| `Language` | Source language or `auto`. |
| `EnableTranslation`, `TranslationProvider`, `TranslationTargetLanguage` | `docker` (free), `google`, or `deepl`. |
| `EnableAssistant` | Claude session assistant on/off. |

Backend behavior is controlled by env vars on the Docker container (see `whisper.cmd` → Manage API):

| Env | Notes |
|---|---|
| `WHISPER_MODEL` | Model preloaded at startup. |
| `NUM_WORKERS` | Concurrent transcriptions. GPU: 2. CPU: 1 (more just splits cores). |
| `LOG_RESULT` | `true` streams each transcription's text to the container logs for monitoring. |

---

## How it works

1. **Capture** — `RecordingService` records system audio via WASAPI loopback and chunks it into in-memory WAV.
2. **Transcribe** — `TranscriptionClient` POSTs each chunk to the Docker Whisper API (`/transcribe`).
3. **Display** — `SubtitleService` de-duplicates and streams SRT to disk; the UI and overlay bind to `TranscriptViewModel`.
4. **Translate** — `TranslationService` translates each segment in the background and fills it into the caption in place.

The Docker container runs `api_server.py` (FastAPI + faster-whisper). Transcription is the heavy step; use a GPU for real-time performance.

## Whisper Models

| Model | VRAM (GPU) | Notes |
|-------|-----------|-------|
| `small` | ~2 GB | Default — near real-time on CPU |
| `medium` | ~5 GB | More accurate; GPU recommended |
| `turbo` | ~8 GB | Fast + accurate (GPU) |
| `large-v3` | 10–15 GB | Highest accuracy (GPU) |

---

## Legacy: batch subtitle burner

The project's original workflow — transcribe a folder of videos and **burn** the subtitles into MP4 with ffmpeg — is kept as a **separate legacy component**, documented on its own so it isn't confused with the real-time app:

➡️ **[docs/legacy-whisper-burner.md](docs/legacy-whisper-burner.md)**

It still ships in `scripts/batch/` and works, but it is no longer the focus.

---

## Project structure

```
whisper-burner/
├── whisper.cmd            # interactive launcher (app, API, batch, models)
├── docker/
│   ├── Dockerfile         # python:3.12-slim + ffmpeg + faster-whisper
│   ├── docker-compose.yml # gpu / cpu profiles
│   ├── api_server.py      # FastAPI ASR + translate API (the app's backend)
│   ├── batch_transcribe.py
│   └── translate_srt.py
├── src/WhisperLive/       # WinUI 3 real-time translation app (primary product)
├── scripts/
│   ├── app/               # build-release.cmd, run-app.cmd, create-shortcut.cmd
│   ├── api/               # start-whisper-{cpu,gpu}.cmd
│   ├── batch/             # process-videos.ps1 + launchers (legacy burner)
│   └── models/            # download-models.ps1
├── docs/superpowers/specs/  # design specs
├── videos/                # legacy batch input; output/ holds generated SRT + MP4
└── models/                # Whisper model cache (auto-populated)
```

---

## Troubleshooting

**Captions lag behind / stop appearing** — the backend is slower than real-time. Use `small` on CPU or switch to the GPU profile; watch `LOG_RESULT=true` logs to confirm throughput.

**`docker compose` can't connect** — Docker Desktop isn't running.

**API changes not taking effect** — `api_server.py` is volume-mounted; restart the container (`whisper.cmd` → Manage API → **Reset**), no rebuild needed. Rebuild only after `Dockerfile`/dependency changes.

---

## References

- [OpenAI Whisper](https://github.com/openai/whisper) · [faster-whisper](https://github.com/SYSTRAN/faster-whisper)
- [Windows App SDK / WinUI 3](https://learn.microsoft.com/windows/apps/winui/winui3/)
- [NVIDIA Container Toolkit](https://docs.nvidia.com/datacenter/cloud-native/container-toolkit/install-guide.html) · [Docker Desktop](https://www.docker.com/products/docker-desktop/)
