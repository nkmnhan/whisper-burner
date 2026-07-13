"""Batch transcription helper using faster-whisper.
Drop-in replacement for the openai-whisper CLI used by process-videos.ps1.
"""
import argparse
import os

from faster_whisper import WhisperModel

_MODEL_NAME_MAP = {"turbo": "large-v3-turbo"}


def format_time(seconds: float) -> str:
    total_ms = round(seconds * 1000)
    ms = total_ms % 1000
    s  = (total_ms // 1000) % 60
    m  = (total_ms // 60_000) % 60
    h  = total_ms // 3_600_000
    return f"{h:02d}:{m:02d}:{s:02d},{ms:03d}"


def write_srt(segments, path: str) -> None:
    os.makedirs(os.path.dirname(path) or ".", exist_ok=True)
    with open(path, "w", encoding="utf-8") as f:
        for i, seg in enumerate(segments, 1):
            f.write(f"{i}\n{format_time(seg.start)} --> {format_time(seg.end)}\n{seg.text.strip()}\n\n")


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("audio")
    parser.add_argument("--model", default=os.environ.get("WHISPER_MODEL", "small"))
    parser.add_argument("--language", default=None)
    parser.add_argument("--task", default="transcribe", choices=["transcribe", "translate"])
    parser.add_argument("--output_dir", default=".")
    parser.add_argument("--output_format", default="srt")
    args = parser.parse_args()

    device = os.environ.get("DEVICE", "cpu")
    compute_type = os.environ.get("COMPUTE_TYPE", "int8")
    # 1 = greedy (fastest), 5 = beam search
    beam_size = int(os.environ.get("BEAM_SIZE", "5"))
    model_cache = os.environ.get("MODEL_CACHE") or None

    fw_model = _MODEL_NAME_MAP.get(args.model, args.model)
    print(f"Loading model '{args.model}' (device={device}, compute_type={compute_type})…", flush=True)
    model = WhisperModel(fw_model, device=device, compute_type=compute_type, download_root=model_cache)

    print(f"Transcribing '{args.audio}'…", flush=True)
    segments_iter, info = model.transcribe(
        args.audio,
        language=args.language or None,
        task=args.task,
        beam_size=beam_size,
    )
    segments = list(segments_iter)
    print(f"Language: {info.language} ({info.language_probability:.2f})", flush=True)

    base = os.path.splitext(os.path.basename(args.audio))[0]
    out_path = os.path.join(args.output_dir, f"{base}.srt")
    write_srt(segments, out_path)
    print(f"Saved: {out_path}", flush=True)


if __name__ == "__main__":
    main()
