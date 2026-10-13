"""Convert a `lockpick-probe trace` log into a recording fixture (JSON Lines).

Applies the same normalization rules as Re1Session so the fixture is a faithful,
reviewable transcription of what the live game exposed. Usage:
    python trace_to_fixture.py trace.log out.jsonl
"""
import json, re, sys
from datetime import datetime

LINE = re.compile(r"^\[(\d\d:\d\d:\d\d\.\d{3})\] (.*?)(?: \| .*)?$")
PAIR = re.compile(r"(\w+)=0x([0-9A-F]+)")

def signed(v, bits):
    v &= (1 << bits) - 1
    return v - (1 << bits) if v >> (bits - 1) else v

def field(status, value=None):
    # Non-valid fields carry no value; the reader falls back to the type's default, like the recorder.
    return {"status": status} if value is None else {"status": status, "value": value}

def snapshot(seq, at, raw):
    in_game = raw["mainMenu"] == 0 and raw["maxHp"] > 0 and signed(raw["hp"], 16) > 0
    def f(value):
        return field("valid", value) if in_game else field("unavailable")
    stage, room = raw["stage"], raw["room"]
    char = {0: "chris", 1: "jill", 3: "rebecca"}.get(raw["character"] & 3, "unknown")
    return {
        "sequence": seq,
        "capturedAt": at,
        "inGameplay": field("valid", in_game),
        "character": f(char),
        "health": f({"current": signed(raw["hp"], 16), "maximum": raw["maxHp"] or None}),
        "location": f({"stage": stage + 1, "room": room}) if stage < 7 and room <= 0x1F else field("unavailable"),
        "camera": f(raw["camera"]),
        "position": f({"x": signed(raw["positionX"], 32), "y": signed(raw["positionY"], 32), "z": signed(raw["positionZ"], 32)}),
        "gameTime": f(f"{raw['gameTimer'] / 30.0:.7f}"),
    }

def to_timespan(seconds):
    s = float(seconds)
    h, rem = divmod(s, 3600)
    m, sec = divmod(rem, 60)
    return f"{int(h):02d}:{int(m):02d}:{sec:010.7f}"

def main(src, dst):
    raw, frames, start, prev_state = {}, [], None, None
    for line in open(src, encoding="utf-8"):
        m = LINE.match(line.strip())
        if not m:
            continue
        t = datetime.strptime(m.group(1), "%H:%M:%S.%f")
        start = start or t
        # The trace prints the full state on the first line, then only changed fields (timer/position
        # appear after '|'); parse both halves.
        for k, v in PAIR.findall(line):
            if k in ("timer",):
                k = "gameTimer"
            raw[k] = int(v, 16)
        for k, a, b in re.findall(r"(pos)=0x([0-9A-F]+),0x([0-9A-F]+)", line):
            raw["positionX"], raw["positionZ"] = int(a, 16), int(b, 16)
        raw.setdefault("positionY", 0)
        offset = (t - start).total_seconds() * 1000
        at = f"2026-10-12T{m.group(1)}0000-07:00"
        snap = snapshot(len(frames) + 1, at, raw)
        if snap["gameTime"]["status"] == "valid":
            snap["gameTime"]["value"] = to_timespan(snap["gameTime"]["value"])
        state = json.dumps({k: v for k, v in snap.items() if k not in ("sequence", "capturedAt")}, sort_keys=True)
        if state != prev_state:
            frames.append({"offsetMs": round(offset, 1), "snapshot": snap})
            prev_state = state
    header = {"format": "lockpick-recording", "version": 1, "gameId": "re1", "profileId": "re1-gog-usa-hotfix4", "recordedAt": "2026-10-12T20:53:52-07:00"}
    with open(dst, "w", encoding="utf-8", newline="\n") as out:
        out.write(json.dumps(header) + "\n")
        for fr in frames:
            out.write(json.dumps(fr, separators=(",", ":")) + "\n")
    print(f"{len(frames)} frames -> {dst}")

if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2])
