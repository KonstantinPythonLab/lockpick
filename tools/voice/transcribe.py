"""Transcribes the game's English voice-over (and optionally its videos) with Whisper, locally.

    python tools/voice/transcribe.py "G:\\GOG Games\\Resident Evil" [--movies] [--model large-v3]

Writes to %LOCALAPPDATA%\\Lockpick\\research\\voice\\ (transcripts.jsonl, one line per file, and transcripts.md grouped
by scene). Transcripts are the game's script: they stay on this PC and are never committed. Re-running skips files
already transcribed. Needs a Python environment with openai-whisper and PyTorch (a CUDA GPU makes it fast), plus
ffmpeg on PATH; see docs/DEVELOPMENT.md.
"""
from __future__ import annotations

import argparse
import json
import os
import re
import sys
import time
from pathlib import Path

OUT = Path(os.environ.get("LOCALAPPDATA", ".")) / "Lockpick" / "research" / "voice"
# Spelling hints for names Whisper wouldn't know.
PROMPT = ("S.T.A.R.S. Alpha and Bravo teams in the Spencer mansion: Jill Valentine, Chris Redfield, Barry Burton, "
          "Albert Wesker, Rebecca Chambers, Brad Vickers, Joseph, Enrico Marini, Richard Aiken, Kenneth, Forest, "
          "Umbrella, Tyrant, Raccoon City.")
VOICE_NAME = re.compile(r"^v(\d+)_([0-9a-z]+)\.wav$", re.I)


def sources(install: Path, movies: bool) -> list[Path]:
    usa = install / "USA" if (install / "USA").is_dir() else install
    files = sorted((usa / "Voice").glob("*.wav"), key=lambda p: p.name.lower())
    if movies:
        files += sorted((usa / "Movie").glob("*.avi"), key=lambda p: p.name.lower())
    return files


def main(argv: list[str]) -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("install", type=Path)
    parser.add_argument("--movies", action="store_true", help="also transcribe the AVI videos")
    parser.add_argument("--model", default="large-v3")
    parser.add_argument("--out", type=Path, default=OUT)
    args = parser.parse_args(argv)

    import torch  # noqa: PLC0415 - heavy imports only when transcribing
    import whisper  # noqa: PLC0415

    args.out.mkdir(parents=True, exist_ok=True)
    jsonl = args.out / "transcripts.jsonl"
    done = set()
    if jsonl.exists():
        done = {json.loads(line)["file"] for line in jsonl.read_text(encoding="utf-8").splitlines() if line.strip()}
    todo = [p for p in sources(args.install, args.movies) if f"{p.parent.name}/{p.name}" not in done]
    device = "cuda" if torch.cuda.is_available() else "cpu"
    print(f"{len(todo)} files to transcribe on {device} ({torch.cuda.get_device_name(0) if device == 'cuda' else 'CPU'})")
    if not todo:
        write_markdown(jsonl, args.out / "transcripts.md")
        return 0

    model = whisper.load_model(args.model, device=device)
    started = time.time()
    with jsonl.open("a", encoding="utf-8") as out:
        for n, path in enumerate(todo, 1):
            audio = whisper.load_audio(str(path))
            result = model.transcribe(audio, language="en", initial_prompt=PROMPT, condition_on_previous_text=False,
                                      fp16=device == "cuda", verbose=None)
            m = VOICE_NAME.match(path.name)
            entry = {
                "file": f"{path.parent.name}/{path.name}",
                "scene": m.group(1) if m else None,
                "line": m.group(2) if m else None,
                "seconds": round(len(audio) / whisper.audio.SAMPLE_RATE, 2),
                "text": result["text"].strip(),
                "segments": [{"start": round(s["start"], 2), "end": round(s["end"], 2), "text": s["text"].strip(),
                              "noSpeech": round(s.get("no_speech_prob", 0), 2)} for s in result["segments"]],
            }
            out.write(json.dumps(entry, ensure_ascii=False) + "\n")
            out.flush()
            if n % 25 == 0 or n == len(todo):
                print(f"{n}/{len(todo)} ({time.time() - started:.0f} s) {path.name}: {entry['text'][:70]}", flush=True)
    write_markdown(jsonl, args.out / "transcripts.md")
    print(f"done in {time.time() - started:.0f} s -> {args.out}")
    return 0


def write_markdown(jsonl: Path, md: Path) -> None:
    entries = [json.loads(line) for line in jsonl.read_text(encoding="utf-8").splitlines() if line.strip()]
    lines = ["# Resident Evil voice-over transcripts (local, Whisper)", ""]
    scene = object()
    for e in sorted(entries, key=lambda e: e["file"].lower()):
        if e["scene"] != scene:
            scene = e["scene"]
            lines += ["", f"## {'Scene ' + scene if scene else 'Other'}", ""]
        lines.append(f"- `{e['file'].split('/')[-1]}` ({e['seconds']} s): {e['text'] or '(no speech)'}")
    md.write_text("\n".join(lines) + "\n", encoding="utf-8")


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
