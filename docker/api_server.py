import asyncio
import logging
import os
import re
import tempfile
import time
from contextlib import asynccontextmanager

from faster_whisper import WhisperModel
from deep_translator import GoogleTranslator
from fastapi import FastAPI, Form, UploadFile
from fastapi.responses import JSONResponse

# ── Structured logging ────────────────────────────────────────────────────────
logging.basicConfig(
    level=logging.INFO,
    format="%(asctime)s [%(levelname)s] %(message)s",
    datefmt="%H:%M:%S",
)
log = logging.getLogger("whisper-api")

_AVAILABLE_MODELS = ["tiny", "base", "small", "medium", "large-v3", "turbo"]
_MODEL_NAME_MAP = {"turbo": "large-v3-turbo"}

_DEFAULT_MODEL = os.environ.get("WHISPER_MODEL", "small")
if _DEFAULT_MODEL not in _AVAILABLE_MODELS:
    raise ValueError(f"WHISPER_MODEL={_DEFAULT_MODEL!r} is not valid; choose from {_AVAILABLE_MODELS}")
_DEVICE       = os.environ.get("DEVICE", "cpu")
_COMPUTE_TYPE = os.environ.get("COMPUTE_TYPE", "int8")
_BEAM_SIZE    = int(os.environ.get("BEAM_SIZE", "1"))
_MODEL_CACHE  = os.environ.get("MODEL_CACHE") or None

_loaded: dict[str, WhisperModel] = {}

# Request counter for correlation across log lines
_req_id = 0


def _next_req_id() -> int:
    global _req_id
    _req_id += 1
    return _req_id


def _get_model(name: str) -> WhisperModel:
    if name not in _loaded:
        fw_name = _MODEL_NAME_MAP.get(name, name)
        log.info("Loading model '%s' (device=%s compute=%s)…", fw_name, _DEVICE, _COMPUTE_TYPE)
        _loaded[name] = WhisperModel(
            fw_name,
            device=_DEVICE,
            compute_type=_COMPUTE_TYPE,
            download_root=_MODEL_CACHE,
        )
        log.info("Model '%s' ready.", fw_name)
    return _loaded[name]


def _run_transcription(model: WhisperModel, path: str, **kwargs) -> list:
    """Consume the faster-whisper generator fully (CPU-bound — runs in thread pool)."""
    segments, _ = model.transcribe(path, **kwargs)
    return list(segments)


_RE_CLEAN = re.compile(
    r'(?P<space>(?:\s*\.\s*){3,}| \. |\s{2,})'
    r'|(?P<drop>_+|\bndn\b|\s+[.,]\s*$|^[.,]\s+)',
    re.MULTILINE,
)


def _clean_text(text: str) -> str:
    return _RE_CLEAN.sub(
        lambda m: ' ' if m.lastgroup == 'space' else '', text
    ).strip()


@asynccontextmanager
async def lifespan(app: FastAPI):
    log.info(
        "Starting up — model=%s device=%s compute=%s beam_size=%d cache=%s",
        _DEFAULT_MODEL, _DEVICE, _COMPUTE_TYPE, _BEAM_SIZE, _MODEL_CACHE,
    )
    _get_model(_DEFAULT_MODEL)
    log.info("Ready to serve.")
    yield
    log.info("Shutting down.")


app = FastAPI(lifespan=lifespan)


@app.get("/health")
def health():
    return {
        "status": "ok",
        "engine": "faster-whisper",
        "device": _DEVICE,
        "compute_type": _COMPUTE_TYPE,
        "beam_size": _BEAM_SIZE,
        "loaded_models": list(_loaded.keys()),
    }


@app.get("/models")
def models():
    return {"available": _AVAILABLE_MODELS, "default": _DEFAULT_MODEL}


@app.post("/transcribe")
async def transcribe(
    file: UploadFile,
    model: str = Form(_DEFAULT_MODEL),
    language: str = Form("auto"),
    initial_prompt: str = Form(""),
    task: str = Form("transcribe"),
):
    req = _next_req_id()

    if model not in _AVAILABLE_MODELS:
        return JSONResponse({"error": "unknown_model"}, status_code=400)
    if task not in ("transcribe", "translate"):
        return JSONResponse({"error": "task must be 'transcribe' or 'translate'"}, status_code=400)

    audio = await file.read()
    audio_kb = len(audio) / 1024
    suffix = os.path.splitext(file.filename or ".wav")[1] or ".wav"

    log.info("[#%d] transcribe  model=%s lang=%s size=%.1fKB", req, model, language, audio_kb)

    with tempfile.NamedTemporaryFile(suffix=suffix, delete=False) as tmp:
        tmp.write(audio)
        tmp_path = tmp.name

    t0 = time.perf_counter()
    try:
        kwargs = {
            "language": None if language in ("auto", "") else language,
            "task": task,
            "beam_size": _BEAM_SIZE,
            "vad_filter": True,
        }
        if initial_prompt:
            kwargs["initial_prompt"] = initial_prompt

        # Run CPU-bound inference in thread pool. Client semaphore(1) guarantees
        # only one request is in-flight at a time, so no concurrent model access.
        seg_list = await asyncio.to_thread(
            lambda: _run_transcription(_get_model(model), tmp_path, **kwargs)
        )
    finally:
        os.unlink(tmp_path)

    elapsed_ms = (time.perf_counter() - t0) * 1000

    segments = []
    for i, s in enumerate(seg_list, 1):
        raw  = s.text
        text = _clean_text(raw)
        if not text:
            log.debug("[#%d] segment %d empty after clean: %r", req, i, raw)
            continue
        if text != raw.strip():
            log.debug("[#%d] segment %d cleaned: %r -> %r", req, i, raw, text)
        segments.append({"id": i, "start": round(s.start, 3), "end": round(s.end, 3), "text": text})

    log.info(
        "[#%d] done  %.0fms  %d seg(s)%s",
        req, elapsed_ms, len(segments),
        f"  [{'; '.join(s['text'][:40] for s in segments[:2])}]" if segments else "  [silence/noise]",
    )

    return {"text": " ".join(s["text"] for s in segments), "segments": segments}


@app.post("/translate")
async def translate(
    text: str = Form(...),
    target: str = Form("vi"),
):
    req = _next_req_id()
    log.info("[#%d] translate  target=%s  text=%r", req, target, text[:60])

    t0 = time.perf_counter()
    try:
        result = await asyncio.to_thread(
            GoogleTranslator(source="auto", target=target).translate, text
        )
        translated = result or text
    except Exception as exc:
        elapsed_ms = (time.perf_counter() - t0) * 1000
        log.warning("[#%d] translate FAILED %.0fms: %s", req, elapsed_ms, exc)
        return {"text": text}

    elapsed_ms = (time.perf_counter() - t0) * 1000
    log.info("[#%d] translated %.0fms  %r -> %r", req, elapsed_ms, text[:40], translated[:40])
    return {"text": translated}


def _get_model(name: str) -> WhisperModel:
    if name not in _loaded:
        fw_name = _MODEL_NAME_MAP.get(name, name)
        _loaded[name] = WhisperModel(
            fw_name,
            device=_DEVICE,
            compute_type=_COMPUTE_TYPE,
            download_root=_MODEL_CACHE,
        )
    return _loaded[name]


def _run_transcription(model: WhisperModel, path: str, **kwargs) -> list:
    """Consume the faster-whisper generator fully (CPU-bound — runs in thread pool)."""
    segments, _ = model.transcribe(path, **kwargs)
    return list(segments)


# Matches faster-whisper output artifacts in one pass.
# group 'space'  → replace with ' '  (spaced-dot filler, lone dot, multi-space)
# group 'drop'   → replace with ''   (blank tokens, 'ndn' hallucination, orphaned punct)
_RE_CLEAN = re.compile(
    r'(?P<space>(?:\s*\.\s*){3,}| \. |\s{2,})'
    r'|(?P<drop>_+|\bndn\b|\s+[.,]\s*$|^[.,]\s+)',
    re.MULTILINE,
)


def _clean_text(text: str) -> str:
    return _RE_CLEAN.sub(
        lambda m: ' ' if m.lastgroup == 'space' else '', text
    ).strip()


@asynccontextmanager
async def lifespan(app: FastAPI):
    global _inference_lock
    _inference_lock = asyncio.Lock()
    print(
        f"[startup] pre-loading model '{_DEFAULT_MODEL}' "
        f"(device={_DEVICE}, compute_type={_COMPUTE_TYPE}, cache={_MODEL_CACHE})…",
        flush=True,
    )
    _get_model(_DEFAULT_MODEL)
    print(f"[startup] model ready", flush=True)
    yield


app = FastAPI(lifespan=lifespan)


@app.get("/health")
def health():
    return {
        "status": "ok",
        "engine": "faster-whisper",
        "device": _DEVICE,
        "compute_type": _COMPUTE_TYPE,
        "loaded_models": list(_loaded.keys()),
    }


@app.get("/models")
def models():
    return {"available": _AVAILABLE_MODELS, "default": _DEFAULT_MODEL}


@app.post("/transcribe")
async def transcribe(
    file: UploadFile,
    model: str = Form(_DEFAULT_MODEL),
    language: str = Form("auto"),
    initial_prompt: str = Form(""),
    task: str = Form("transcribe"),
):
    if model not in _AVAILABLE_MODELS:
        return JSONResponse({"error": "unknown_model"}, status_code=400)
    if task not in ("transcribe", "translate"):
        return JSONResponse({"error": "task must be 'transcribe' or 'translate'"}, status_code=400)

    audio = await file.read()
    suffix = os.path.splitext(file.filename or ".wav")[1] or ".wav"

    with tempfile.NamedTemporaryFile(suffix=suffix, delete=False) as tmp:
        tmp.write(audio)
        tmp_path = tmp.name

    try:
        kwargs = {
            "language": None if language in ("auto", "") else language,
            "task": task,
            "beam_size": _BEAM_SIZE,
            "vad_filter": True,
        }
        if initial_prompt:
            kwargs["initial_prompt"] = initial_prompt

        # Serialize inference: faster-whisper model is not thread-safe.
        # Requests queue here so the model processes one chunk at a time.
        async with _inference_lock:
            seg_list = await asyncio.to_thread(
                lambda: _run_transcription(_get_model(model), tmp_path, **kwargs)
            )
    finally:
        os.unlink(tmp_path)

    segments = []
    for i, s in enumerate(seg_list, 1):
        raw = s.text
        text = _clean_text(raw)
        if not text:
            print(f"[blank] Segment {i} empty after cleaning: {raw!r}", flush=True)
            continue
        if text != raw.strip():
            print(f"[blank] Cleaned segment {i}: {raw!r} → {text!r}", flush=True)
        segments.append({"id": i, "start": s.start, "end": s.end, "text": text})
    return {"text": " ".join(s["text"] for s in segments), "segments": segments}


@app.post("/translate")
async def translate(
    text: str = Form(...),
    target: str = Form("vi"),
):
    translated = await asyncio.to_thread(
        GoogleTranslator(source="auto", target=target).translate, text
    )
    return {"text": translated or text}
