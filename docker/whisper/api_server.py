import asyncio
import os
import tempfile
from contextlib import asynccontextmanager

import torch
import whisper
from deep_translator import GoogleTranslator
from fastapi import FastAPI, Form, UploadFile
from fastapi.responses import JSONResponse

_AVAILABLE_MODELS = ["tiny", "base", "small", "medium", "large-v3", "turbo"]
_DEFAULT_MODEL = os.environ.get("WHISPER_MODEL", "small")
_loaded: dict = {}


def _get_model(name: str):
    if name not in _loaded:
        _loaded[name] = whisper.load_model(name)
    return _loaded[name]


@asynccontextmanager
async def lifespan(app: FastAPI):
    print(f"[startup] pre-loading model '{_DEFAULT_MODEL}'…", flush=True)
    _get_model(_DEFAULT_MODEL)
    print(f"[startup] model ready", flush=True)
    yield


app = FastAPI(lifespan=lifespan)


@app.get("/health")
def health():
    return {
        "status": "ok",
        "engine": "whisper",
        "gpu": torch.cuda.is_available(),
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
        # language=None triggers Whisper auto-detect; task="translate" outputs English
        kwargs = {"language": None if language in ("auto", "") else language, "task": task}
        if initial_prompt:
            kwargs["initial_prompt"] = initial_prompt
        result = await asyncio.to_thread(_get_model(model).transcribe, tmp_path, **kwargs)
    finally:
        os.unlink(tmp_path)

    segments = [
        {"id": i, "start": s["start"], "end": s["end"], "text": s["text"].strip()}
        for i, s in enumerate(result["segments"], 1)
    ]
    return {"text": result["text"].strip(), "segments": segments}


@app.post("/translate")
async def translate(
    text: str = Form(...),
    target: str = Form("vi"),
):
    translated = await asyncio.to_thread(
        GoogleTranslator(source="auto", target=target).translate, text
    )
    return {"text": translated or text}
