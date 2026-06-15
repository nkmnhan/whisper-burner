# syntax=docker/dockerfile:1

FROM python:3.12-slim

ARG VARIANT=cpu

RUN --mount=type=cache,target=/var/cache/apt,sharing=locked \
    --mount=type=cache,target=/var/lib/apt,sharing=locked \
    apt-get update && DEBIAN_FRONTEND=noninteractive apt-get install -y --no-install-recommends \
    ffmpeg

RUN --mount=type=cache,target=/root/.cache/pip \
    pip install uv

RUN --mount=type=cache,target=/root/.cache/uv \
    if [ "$VARIANT" = "gpu" ]; then \
        uv pip install --system \
            faster-whisper nvidia-cublas-cu12 nvidia-cudnn-cu12 \
            deep-translator fastapi "uvicorn[standard]" python-multipart; \
    else \
        uv pip install --system \
            faster-whisper \
            deep-translator fastapi "uvicorn[standard]" python-multipart; \
    fi

WORKDIR /app

CMD ["uvicorn", "api_server:app", "--host", "0.0.0.0", "--port", "5000"]
