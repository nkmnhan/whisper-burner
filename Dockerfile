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
            --extra-index-url https://download.pytorch.org/whl/cu124 \
            torch openai-whisper deep-translator; \
    else \
        uv pip install --system \
            --extra-index-url https://download.pytorch.org/whl/cpu \
            torch openai-whisper deep-translator; \
    fi

WORKDIR /app

CMD ["whisper"]
