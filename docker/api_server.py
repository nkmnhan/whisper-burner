import asyncio
import os
import tempfile
from contextlib import asynccontextmanager

from faster_whisper import WhisperModel
from deep_translator import GoogleTranslator
from fastapi import FastAPI, Form, UploadFile
from fastapi.responses import JSONResponse

_AVAILABLE_MODELS = ["tiny", "base", "small", "medium", "large-v3", "turbo"]
# faster-whisper uses a different model name for turbo
_MODEL_NAME_MAP = {"turbo": "large-v3-turbo"}

_DEFAULT_MODEL = os.environ.get("WHISPER_MODEL", "small")
if _DEFAULT_MODEL not in _AVAILABLE_MODELS:
    raise ValueError(f"WHISPER_MODEL={_DEFAULT_MODEL!r} is not valid; choose from {_AVAILABLE_MODELS}")
_DEVICE = os.environ.get("DEVICE", "cpu")
_COMPUTE_TYPE = os.environ.get("COMPUTE_TYPE", "int8")
# 1 = greedy (fastest), 5 = beam search
_BEAM_SIZE = int(os.environ.get("BEAM_SIZE", "5"))
_MODEL_CACHE = os.environ.get("MODEL_CACHE") or None

_loaded: dict[str, WhisperModel] = {}


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


@asynccontextmanager
async def lifespan(app: FastAPI):
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
        }
        if initial_prompt:
            kwargs["initial_prompt"] = initial_prompt

        # Run CPU-bound transcription off the async event loop.
        # _get_model is inside the lambda so model loading also happens off the event loop.
        seg_list = await asyncio.to_thread(
            lambda: _run_transcription(_get_model(model), tmp_path, **kwargs)
        )
    finally:
        os.unlink(tmp_path)

    segments = [
        {"id": i, "start": s.start, "end": s.end, "text": s.text.strip()}
        for i, s in enumerate(seg_list, 1)
    ]
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
