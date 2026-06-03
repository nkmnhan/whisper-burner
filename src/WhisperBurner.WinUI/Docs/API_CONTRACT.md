# Docker Whisper API Contract

Proposed local HTTP API to expose the existing Docker Whisper stack for use by the WinUI app.

Base URL: `http://localhost:5000` (configurable in Settings).

---

## GET /health

Returns service health and backend metadata.

**Response 200**
```json
{
  "status": "ok",
  "engine": "whisper",
  "gpu": true
}
```

---

## GET /models

Returns available Whisper models.

**Response 200**
```json
{
  "available": ["tiny", "base", "small", "medium", "large-v3", "turbo"],
  "default": "small"
}
```

---

## POST /transcribe

Accepts an audio chunk or full file and returns text plus subtitle segments.

**Request**
- `Content-Type: multipart/form-data`
- Field `file`: WAV or PCM audio data
- Field `model`: model name (string, optional — defaults to server default)
- Field `language`: language code (string, optional — e.g. `"en"`)

**Response 200**
```json
{
  "text": "hello world",
  "segments": [
    {
      "id": 1,
      "start": 0.0,
      "end": 2.4,
      "text": "hello world"
    }
  ]
}
```

**Response 503** — Whisper engine not ready
```json
{ "error": "engine_not_ready" }
```

---

## Streaming (future design only)

Not implemented in MVP. Two candidate designs for future phases:

- `POST /stream/chunk` — stateless chunk endpoint with session token header
- WebSocket `/stream/ws` — push segments as they are ready

MVP uses short-chunk polling via `POST /transcribe` (2–5 s chunks).

---

## Notes for implementors

The Docker service needs a lightweight Python HTTP wrapper around the existing Whisper batch logic. FastAPI is the recommended choice for minimal boilerplate.

The `/transcribe` endpoint should accept audio in WAV format to avoid needing ffmpeg inside the API request path (ffmpeg is used only for burn-in, not transcription input).
