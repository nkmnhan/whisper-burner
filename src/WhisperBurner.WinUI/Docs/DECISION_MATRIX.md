# Technology Decision Matrix

## UI framework

| Option | Windows UX fit | Floating subtitle window | Packaging | Recommendation |
|---|---|---|---|---|
| WinUI 3 | Excellent | Excellent (AppWindow API) | MSIX or unpackaged | **Selected** |
| WPF | Good | Good (Window AllowsTransparency) | xcopy | Alternative if WinUI 3 blocked |
| Electron | Good | Good | High overhead | Only if web stack preferred |
| MAUI | Fair | Limited | Complex | Not suitable for desktop-first |

WinUI 3 selected because it provides native Windows 11 controls, the `AppWindow` API for borderless always-on-top overlays, and `GraphicsCaptureItem` for screen recording.

---

## Transcription backend

| Option | Integration effort | GPU support | Offline | Packaging | Recommendation |
|---|---|---|---|---|---|
| Docker Whisper API | Low | Yes (existing) | Partial | None | **MVP** |
| Local Whisper (Python) | High | Complex | Yes | Very high | Phase 5 |
| Azure Speech | Low | N/A | No | None | Cost concern |
| Windows Speech | None | N/A | Yes | None | Accuracy concern |

Docker API selected for MVP: reuses the existing repo investment, keeps GPU experimentation separate from the desktop app, and avoids model packaging complexity in v1.

---

## Screen capture API

| Option | GPU acceleration | Audio | Cursor | Notes |
|---|---|---|---|---|
| Windows.Graphics.Capture (WGC) | Yes | No | Optional | **Recommended** — modern, GPU zero-copy |
| DXGI Desktop Duplication | Yes | No | No | Lower level, more complex |
| GDI BitBlt | No | No | Optional | Legacy, slow for high FPS |

WGC is the recommended capture API. It does not capture audio; audio capture requires a separate WASAPI or Windows Audio Session API implementation.

---

## Audio capture

| Option | System audio | Mic | Complexity |
|---|---|---|---|
| WASAPI loopback | Yes | No | Medium |
| WASAPI capture | No | Yes | Low |
| NAudio (wrapper) | Both | Both | Low |

MVP: mic-only capture via WASAPI is the lowest-risk first path. System audio loopback is opt-in in Phase 2+.

---

## Confirmed decisions

1. WinUI 3 is the desktop UI framework.
2. Docker API is the first transcription backend.
3. Floating subtitles use a separate always-on-top borderless window.
4. Subtitle burn-in is optional and deferred — never at recording time.
5. Sessions are stored as evidence folders with a `manifest.json`.
6. Replay and subtitle preview happen before any burn-in export.
7. Original `recording.mp4` is never overwritten.
