import re
import sys
import time
from deep_translator import GoogleTranslator

SEPARATOR = " ||| "
MAX_CHARS = 4500


def parse_srt(content: str) -> list[tuple[str, str, str]]:
    blocks = re.split(r"\n\n+", content.strip())
    result = []
    for block in blocks:
        lines = block.split("\n")
        if len(lines) < 3:
            result.append(("", "", block))
        else:
            result.append((lines[0], lines[1], "\n".join(lines[2:])))
    return result


def make_chunks(texts: list[str]) -> list[list[int]]:
    chunks, current, current_len = [], [], 0
    for i, text in enumerate(texts):
        added = len(SEPARATOR) * bool(current) + len(text)
        if current and current_len + added > MAX_CHARS:
            chunks.append(current)
            current, current_len = [i], len(text)
        else:
            current.append(i)
            current_len += added
    if current:
        chunks.append(current)
    return chunks


def translate_texts(texts: list[str], target: str) -> list[str]:
    translator = GoogleTranslator(source="auto", target=target)
    results = [""] * len(texts)
    chunks = make_chunks(texts)

    for chunk_num, indices in enumerate(chunks, 1):
        print(f"  Translating batch {chunk_num}/{len(chunks)} ({len(indices)} subtitles)...", flush=True)
        batch = [texts[i] for i in indices]
        joined = SEPARATOR.join(batch)
        translated = translator.translate(joined)
        parts = translated.split(SEPARATOR)

        if len(parts) == len(batch):
            for i, part in zip(indices, parts):
                results[i] = part.strip()
        else:
            # Separator didn't survive — fall back to one-by-one for this batch
            print(f"  Batch {chunk_num}: separator mismatch, falling back to individual requests...", flush=True)
            for i, text in zip(indices, batch):
                results[i] = translator.translate(text)
                time.sleep(0.2)

    return results


def translate_srt(input_path: str, output_path: str, target: str) -> None:
    with open(input_path, encoding="utf-8") as f:
        content = f.read()

    blocks = parse_srt(content)
    texts = [text for _, _, text in blocks]
    translatable = [i for i, t in enumerate(texts) if t.strip()]

    print(f"  {len(blocks)} subtitle blocks, {len(translatable)} to translate", flush=True)

    translated = translate_texts([texts[i] for i in translatable], target)
    for idx, result in zip(translatable, translated):
        texts[idx] = result

    out_blocks = []
    for (index, timestamp, _), text in zip(blocks, texts):
        if index:
            out_blocks.append(f"{index}\n{timestamp}\n{text}")
        else:
            out_blocks.append(text)

    with open(output_path, "w", encoding="utf-8") as f:
        f.write("\n\n".join(out_blocks))

    print(f"  Saved to {output_path}", flush=True)


if __name__ == "__main__":
    if len(sys.argv) != 4:
        print("Usage: translate_srt.py <input.srt> <output.srt> <target_lang>")
        sys.exit(1)
    translate_srt(sys.argv[1], sys.argv[2], sys.argv[3])
