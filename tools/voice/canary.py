"""Second-opinion transcription with NVIDIA Canary-Qwen 2.5B (NeMo), to cross-check Whisper.

Runs under Linux / WSL with NVIDIA NeMo installed (see docs/DEVELOPMENT.md):

    python tools/voice/canary.py "/mnt/g/GOG Games/Resident Evil" --out "/mnt/c/Users/<you>/AppData/Local/Lockpick/research/voice"

Writes transcripts-canary.jsonl in the same format as transcribe.py (file, scene, line, seconds, text). Audio is
resampled to 16 kHz mono and cut into 30-second pieces (the model takes up to 40 s at a time). Local only, like the
Whisper transcripts. Re-running skips files already done.
"""
from __future__ import annotations

import argparse
import json
import re
import subprocess
import sys
import tempfile
import time
from pathlib import Path

VOICE_NAME = re.compile(r"^v(\d+)_([0-9a-z]+)\.wav$", re.I)
CHUNK_SECONDS = 30
BATCH = 16


def pieces(path: Path, workdir: Path) -> list[Path]:
    """16 kHz mono WAV pieces of at most CHUNK_SECONDS."""
    stem = workdir / path.stem
    subprocess.run(["ffmpeg", "-v", "error", "-y", "-i", str(path), "-ac", "1", "-ar", "16000", "-f", "segment",
                    "-segment_time", str(CHUNK_SECONDS), f"{stem}_%03d.wav"], check=True)
    return sorted(workdir.glob(f"{path.stem}_*.wav"))


def duration(path: Path) -> float:
    out = subprocess.run(["ffprobe", "-v", "error", "-show_entries", "format=duration", "-of", "csv=p=0", str(path)],
                         capture_output=True, text=True, check=True).stdout.strip()
    return round(float(out or 0), 2)


def main(argv: list[str]) -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("install", type=Path)
    parser.add_argument("--out", type=Path, required=True)
    parser.add_argument("--movies", action="store_true")
    args = parser.parse_args(argv)

    from nemo.collections.speechlm2.models import SALM  # noqa: PLC0415

    usa = args.install / "USA"
    files = sorted((usa / "Voice").glob("*.wav"), key=lambda p: p.name.lower())
    if args.movies:
        files += sorted((usa / "Movie").glob("*.avi"), key=lambda p: p.name.lower())
    jsonl = args.out / "transcripts-canary.jsonl"
    done = set()
    if jsonl.exists():
        done = {json.loads(line)["file"] for line in jsonl.read_text(encoding="utf-8").splitlines() if line.strip()}
    todo = [p for p in files if f"{p.parent.name}/{p.name}" not in done]
    print(f"{len(todo)} files to transcribe with Canary-Qwen", flush=True)

    model = SALM.from_pretrained("nvidia/canary-qwen-2.5b").bfloat16().eval().cuda()
    prompt = f"Transcribe the following: {model.audio_locator_tag}"
    started = time.time()
    with jsonl.open("a", encoding="utf-8") as out, tempfile.TemporaryDirectory() as tmp:
        for n, path in enumerate(todo, 1):
            work = Path(tmp) / str(n)
            work.mkdir()
            chunks = pieces(path, work)
            texts = []
            for i in range(0, len(chunks), BATCH):
                batch = chunks[i:i + BATCH]
                ids = model.generate(
                    prompts=[[{"role": "user", "content": prompt, "audio": [str(c)]}] for c in batch],
                    max_new_tokens=256)
                texts += [model.tokenizer.ids_to_text(row.cpu()).strip() for row in ids]
            m = VOICE_NAME.match(path.name)
            entry = {"file": f"{path.parent.name}/{path.name}", "scene": m.group(1) if m else None,
                     "line": m.group(2) if m else None, "seconds": duration(path), "text": " ".join(t for t in texts if t)}
            out.write(json.dumps(entry, ensure_ascii=False) + "\n")
            out.flush()
            if n % 25 == 0 or n == len(todo):
                print(f"{n}/{len(todo)} ({time.time() - started:.0f} s) {path.name}: {entry['text'][:70]}", flush=True)
    print(f"done in {time.time() - started:.0f} s -> {jsonl}", flush=True)
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
