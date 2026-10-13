"""Capture bounty tooling.

data/bounties.json is the source of truth; docs/CAPTURE_BOUNTIES.md is generated from it.

    python tools/bounties/bounties.py render [--check]          regenerate (or verify) the Markdown list
    python tools/bounties/bounties.py analyze CAPTURE...        report what captures show (zip or trace .txt)
    python tools/bounties/bounties.py apply CAPTURE... [--source NAME]
                                                                record findings in bounties.json, then render
    python tools/bounties/bounties.py triage --body-env VAR --out report.md
                                                                (CI) download zips linked in an issue body, analyze

Only unambiguous evidence checks a bounty off automatically (e.g. Rebecca's character id together with her
inventory). Captures that record trainer memory writes never verify anything automatically.
Everything else is recorded as "evidence — needs review" so a maintainer confirms it.
Captures are untrusted input: zip members are read in memory by exact name with size limits, never extracted.
"""
from __future__ import annotations

import argparse
import datetime as dt
import io
import json
import os
import re
import sys
import urllib.request
import zipfile
from dataclasses import dataclass, field
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
DATA = ROOT / "data" / "bounties.json"
DOC = ROOT / "docs" / "CAPTURE_BOUNTIES.md"

STATUS_ICON = {"done": "✅", "partial": "🟡", "needed": "⬜", "blocked": "🛠️"}

# Item ids already identified from live captures (docs/MEMORY_MAP.md); anything else is reported as new.
KNOWN_ITEM_IDS = {0x01, 0x02, 0x03, 0x05, 0x0B, 0x0E, 0x11, 0x1F, 0x23, 0x26, 0x2D, 0x2F, 0x41, 0x44}

MAX_MEMBER_BYTES = 64 * 1024 * 1024
MAX_TOTAL_BYTES = 256 * 1024 * 1024
MAX_DOWNLOAD_BYTES = 100 * 1024 * 1024
MEMBER_PATTERN = re.compile(r"^(?:[^/]+/)?(trace-\d+\.txt|events-\d+\.txt|capture-info\.json)$")
ATTACHMENT_PATTERN = re.compile(r"https://github\.com/(?:user-attachments/files|[\w.-]+/[\w.-]+/files)/\d+/[\w.%-]+\.zip")
TRACE_LINE = re.compile(r"^\[(\d\d:\d\d:\d\d\.\d{3})\] (.*?)(?: \| (.*))?$")
PAIR = re.compile(r"(\w+)=0x([0-9A-Fa-f]+)")

HEADER = """# Capture bounties

<!-- Generated from data/bounties.json by tools/bounties/bounties.py — edit the JSON, then run `render`. -->

> **Unpaid, captures only, no bots** — see the rules below.

## Bounty rules

1. **Bounties are unpaid.** No money, crypto or other payment is ever offered or owed. The reward is your name in
   Lockpick's in-app credits (Settings → Credits) and in CONTRIBUTORS.md, if you want it.
2. **Bounties are captures, never code changes.** A bounty is completed only by a capture zip of you playing the
   game (Debug → Start capture). Patches, pull requests or "proposed fixes" don't complete bounties and are never
   rewarded. Code contributions are welcome separately under the normal [contributing guide](../CONTRIBUTING.md).
3. **No automated or AI-generated replies.** Comments, issues or pull requests produced by bots or AI tools are hidden
   as spam and the account may be blocked and reported. Payment requests are ignored.

## How it works

Everything Lockpick needs to see the real game do, grouped by the release that needs it. Each bounty is a short
thing to do in-game while a capture is running (**Debug → Start capture**, see [CAPTURE.md](CAPTURE.md)). One capture
can cover many bounties — just play and send the zip with a
[Capture submission](https://github.com/mscrnt/lockpick/issues/new?template=capture-submission.yml) issue.

Submitted captures are analyzed automatically: unambiguous results (for example Rebecca's character id appearing)
are checked off, and everything else is marked as evidence for a maintainer to review. Open bounties for the next
release are also GitHub issues with the [`bounty` label](https://github.com/mscrnt/lockpick/issues?q=is%3Aissue+label%3Abounty).

Status: ✅ verified from a capture · 🟡 partly verified / evidence to review · ⬜ needed · 🛠️ needs a Lockpick feature first

Unless noted, bounties are for the supported build (GOG, USA, 1.0 hotfix4).
"""


# ----------------------------------------------------------------------------------------------------------------
# Data and rendering
# ----------------------------------------------------------------------------------------------------------------

def load_data(path: Path = DATA) -> dict:
    return json.loads(path.read_text(encoding="utf-8"))


def save_data(data: dict, path: Path = DATA) -> None:
    path.write_text(json.dumps(data, ensure_ascii=False, indent=2) + "\n", encoding="utf-8", newline="\n")


def render(data: dict) -> str:
    out = [HEADER]
    for release in data["releases"]:
        out.append(f"\n---\n\n## {release['release']} — {release['title']}\n")
        out.append("| ID | Do this in-game | Verifies | Status |")
        out.append("|---|---|---|---|")
        for b in release["bounties"]:
            status = STATUS_ICON[b["status"]]
            if b.get("evidence"):
                status += " " + "; ".join(b["evidence"])
            out.append(f"| {b['id']} | {b['task']} | {b['verifies']} | {status} |")
    return "\n".join(out) + "\n"


def all_bounties(data: dict):
    for release in data["releases"]:
        yield from release["bounties"]


# ----------------------------------------------------------------------------------------------------------------
# Capture parsing
# ----------------------------------------------------------------------------------------------------------------

def signed(value: int, bits: int) -> int:
    value &= (1 << bits) - 1
    return value - (1 << bits) if value >> (bits - 1) else value


@dataclass
class Session:
    name: str
    states: list[dict[str, int]] = field(default_factory=list)  # full raw state after each trace line
    events: list[str] = field(default_factory=list)


@dataclass
class Capture:
    name: str
    sessions: list[Session] = field(default_factory=list)
    builds: list[dict] = field(default_factory=list)
    # Number of trainer memory writes recorded in the capture (0.2+ captures record this; older ones can't write).
    trainer_writes: int = 0


def parse_trace(name: str, text: str) -> Session:
    session = Session(name)
    state: dict[str, int] = {}
    for line in text.splitlines():
        m = TRACE_LINE.match(line.strip())
        if not m:
            continue
        for key, value in PAIR.findall(m.group(2)):
            state[key] = int(value, 16)
        context = m.group(3) or ""
        if (t := re.search(r"timer=0x([0-9A-Fa-f]+)", context)) is not None:
            state["gameTimer"] = int(t.group(1), 16)
        if (p := re.search(r"pos=0x([0-9A-Fa-f]+),0x([0-9A-Fa-f]+)", context)) is not None:
            state["positionX"], state["positionZ"] = int(p.group(1), 16), int(p.group(2), 16)
        session.states.append(dict(state))
    return session


def read_capture(path: Path) -> Capture:
    if path.suffix.lower() != ".zip":
        return Capture(path.name, [parse_trace(path.name, path.read_text(encoding="utf-8", errors="replace"))])
    return read_capture_zip(path.name, path.read_bytes())


def read_capture_zip(name: str, payload: bytes) -> Capture:
    capture = Capture(name)
    with zipfile.ZipFile(io.BytesIO(payload)) as archive:
        members = [i for i in archive.infolist() if MEMBER_PATTERN.match(i.filename)]
        if sum(i.file_size for i in members) > MAX_TOTAL_BYTES or any(i.file_size > MAX_MEMBER_BYTES for i in members):
            raise ValueError("capture is larger than the analysis limits")
        traces, events = {}, {}
        for info in members:
            base = info.filename.rsplit("/", 1)[-1]
            text = archive.read(info).decode("utf-8", errors="replace")
            if base == "capture-info.json":
                try:
                    info_json = json.loads(text)
                    capture.builds = info_json.get("Builds", [])
                    capture.trainer_writes = int(info_json.get("TrainerWrites", 0) or 0)
                except (json.JSONDecodeError, AttributeError, TypeError, ValueError):
                    capture.builds = []
            elif base.startswith("trace-"):
                traces[base] = text
            elif base.startswith("events-"):
                events[base.replace("events-", "trace-")] = text
        for trace_name in sorted(traces, key=lambda n: int(re.sub(r"\D", "", n) or 0)):
            session = parse_trace(f"{name}/{trace_name}", traces[trace_name])
            session.events = [l for l in events.get(trace_name, "").splitlines() if l.strip()]
            capture.sessions.append(session)
    return capture


# ----------------------------------------------------------------------------------------------------------------
# Detection
# ----------------------------------------------------------------------------------------------------------------

@dataclass
class Finding:
    detector: str
    text: str
    auto: bool  # True: unambiguous, may check the bounty off; False: evidence for review


def in_game(s: dict[str, int]) -> bool:
    return s.get("mainMenu", 1) == 0 and s.get("maxHp", 0) > 0 and signed(s.get("hp", 0), 16) > 0


def detect(capture: Capture) -> tuple[dict[str, Finding], dict[str, object]]:
    """Returns findings keyed by detector name, plus general facts for the report."""
    found: dict[str, Finding] = {}
    rooms: dict[int, set[int]] = {}
    item_ids: set[int] = set()
    facts: dict[str, object] = {}

    def add(detector: str, text: str, auto: bool) -> None:
        found.setdefault(detector, Finding(detector, text, auto))

    restarts = max(0, len(capture.sessions) - 1)
    for session in capture.sessions:
        prev: dict[str, int] | None = None
        died = False
        equipped: set[int] = set()
        last_room = None
        room_changes = 0
        for s in session.states:
            game = in_game(s)
            char = s.get("character", 0) & 3
            hp = signed(s.get("hp", 0), 16)
            if game:
                rooms.setdefault(s.get("stage", 0), set()).add(s.get("room", 0))
                if char == 0:
                    add("chris_game", f"Chris game (character 0, max HP {s.get('maxHp')})", True)
                elif char == 1:
                    add("jill_game", f"Jill game (character 1, max HP {s.get('maxHp')})", True)
                if char == 3:
                    # Both signals appear together in real play; one alone could be a memory edit, so review it.
                    both = s.get("inventoryPtr") == 0xBE9950
                    add("rebecca", "character id 3 with Rebecca's inventory (0xBE9950)" if both else "character id 3 (Rebecca)", both)
                if s.get("stage") == 1:
                    add("second_floor", f"stage index 1 (room 2{s.get('room', 0):02X})", True)
                room = (s.get("stage"), s.get("room"))
                if last_room is not None and room != last_room:
                    room_changes += 1
                last_room = room
                for i in range(8):
                    if (v := s.get(f"inv{i}")) is not None and v & 0xFF:
                        item_ids.add(v & 0xFF)
                for i in range(4):
                    if (v := s.get(f"box{i}")) is not None and v & 0xFF:
                        item_ids.add(v & 0xFF)
                if (slot := s.get("equippedSlot")) is not None and slot > 0:
                    equipped.add(slot)
                slots = [s.get(f"inv{i}", 0) & 0xFF for i in range(8)]
                limit = 8 if char == 1 else 6
                if all(slots[:limit]):
                    add("full_inventory", f"all {limit} inventory slots filled", False)
            if s.get("inventoryPtr") == 0xBE9950 and char != 3:
                add("rebecca", "inventory pointer at Rebecca's inventory (0xBE9950)", False)

            if prev is not None:
                prev_hp = signed(prev.get("hp", 0), 16)
                playing = game and in_game(prev)
                if prev.get("maxHp", 0) > 0 and prev_hp > 0 and hp <= 0:
                    died = True
                    add("death", f"HP {prev_hp} → {hp}", True)
                if died and game and s.get("gameTimer", 1) == 0:
                    add("new_game_after_death", "new game started after a death", True)
                if game and in_game(prev) and hp > prev_hp and s.get("gameTimer", 0) != 0:
                    add("healing", f"HP {prev_hp} → {hp}", False)
                old_t, new_t = prev.get("gameTimer"), s.get("gameTimer")
                if game and old_t is not None and new_t is not None and new_t < old_t and new_t >= 60:
                    add("save_loaded", f"timer restored {old_t / 30:.0f}s → {new_t / 30:.0f}s", True)
                for i in range(8 if playing else 0):
                    key = f"inv{i}"
                    if key in s and key in prev and (s[key] & 0xFF) == (prev[key] & 0xFF) and s[key] & 0xFF:
                        before, after = prev[key] >> 8, s[key] >> 8
                        if after == before - 1 and s.get("equippedSlot") == i + 1:
                            add("fire_and_reload", f"weapon id 0x{s[key] & 0xFF:02X} fired (slot {i + 1})", False)
                for i in range(4 if playing else 0):
                    key = f"box{i}"
                    if key in s and key in prev and s[key] != prev[key]:
                        add("item_box_changed", f"item box entry {i + 1}: 0x{prev[key]:04X} → 0x{s[key]:04X}", False)
            prev = s
        if room_changes >= 2:
            add("room_changes", f"{room_changes} room changes", True)
        if len(equipped) >= 3:
            add("weapon_switching", f"equipped slots used: {sorted(equipped)}", False)
        if any("Save loaded" in e or "New game started" in e for e in session.events):
            restarts += 1
    if restarts:
        add("resets", f"{restarts} restart(s) / reload(s)", False)

    by_label: dict[str, list[str]] = {}
    for stage, room_set in sorted(rooms.items()):
        label = "rooms_stage_6_7" if stage in (5, 6) else f"rooms_stage_{stage + 1}"
        by_label.setdefault(label, []).extend(f"{stage + 1}{r:02X}" for r in sorted(room_set))
    for label, ids in by_label.items():
        found[label] = Finding(label, " ".join(ids), False)

    for build in capture.builds:
        if not build.get("ProfileId"):
            add("other_build", f"unsupported build {build.get('FileName')} {build.get('FileSize')} bytes, SHA-256 {str(build.get('Sha256'))[:16]}…", False)

    if capture.trainer_writes:
        for key, finding in list(found.items()):
            found[key] = Finding(finding.detector, finding.text + " (capture includes trainer writes)", False)

    facts["rooms"] = {stage + 1: sorted(r) for stage, r in sorted(rooms.items())}
    facts["item_ids"] = sorted(item_ids)
    facts["new_item_ids"] = sorted(item_ids - KNOWN_ITEM_IDS)
    facts["builds"] = capture.builds
    facts["sessions"] = len(capture.sessions)
    facts["samples"] = sum(len(s.states) for s in capture.sessions)
    return found, facts


def match(data: dict, findings: dict[str, Finding]) -> list[tuple[dict, Finding]]:
    return [(b, findings[b["detector"]]) for b in all_bounties(data) if b.get("detector") in findings]


# ----------------------------------------------------------------------------------------------------------------
# Report and apply
# ----------------------------------------------------------------------------------------------------------------

def report(data: dict, capture: Capture, findings: dict[str, Finding], facts: dict[str, object]) -> str:
    lines = [f"### Lockpick capture analysis — `{capture.name}`", ""]
    builds = facts["builds"] or []
    for b in builds:
        state = f"supported (`{b.get('ProfileId')}`)" if b.get("ProfileId") else "**not supported yet** — thanks, the fingerprint helps"
        lines.append(f"- Game build: `{b.get('FileName')}` {b.get('FileSize')} bytes, SHA-256 `{str(b.get('Sha256'))[:16]}…` — {state}")
    lines.append(f"- {facts['sessions']} game session(s), {facts['samples']:,} recorded changes")
    matched = match(data, findings)
    auto = [(b, f) for b, f in matched if f.auto and b["status"] != "done"]
    review = [(b, f) for b, f in matched if not f.auto and b["status"] != "done"]
    already = [(b, f) for b, f in matched if b["status"] == "done"]
    if auto:
        lines += ["", "**Verified automatically:**"] + [f"- {b['id']} — {f.text}" for b, f in auto]
    if review:
        lines += ["", "**Evidence for a maintainer to review:**"] + [f"- {b['id']} — {f.text}" for b, f in review]
    if already:
        lines += ["", "Also seen (already verified): " + ", ".join(sorted({b["id"] for b, _ in already}))]
    rooms = facts["rooms"] or {}
    if rooms:
        lines += ["", "**Rooms seen:** " + "; ".join(f"stage {s}: " + " ".join(f"{s}{r:02X}" for r in rs) for s, rs in rooms.items())]
    if facts["item_ids"]:
        new = ", ".join(f"0x{i:02X}" for i in facts["new_item_ids"]) or "none"
        lines.append(f"**Item ids seen:** {', '.join(f'0x{i:02X}' for i in facts['item_ids'])} (new: {new})")
    if not matched and not rooms:
        reason = ("it only contains the title screen or attract-mode demos — start or load a game while capturing"
                  if facts["samples"] else "no game data was recorded — was the game running while capturing?")
        lines += ["", f"No bounty evidence found: {reason}."]
    lines += ["", "_Automated summary. A maintainer confirms results before bounties are checked off. "
              "Bounties are unpaid; the reward is an in-app credit if you want it._"]
    return "\n".join(lines) + "\n"


def apply(data: dict, findings: dict[str, Finding], source: str, today: str) -> list[str]:
    changes = []
    for bounty, finding in match(data, findings):
        evidence = bounty.setdefault("evidence", [])
        if bounty["detector"].startswith("rooms_stage"):
            seen = set()
            for e in evidence:
                seen.update(re.findall(r"\b[1-7][0-9A-F]{2}\b", e))
            seen.update(finding.text.split())
            merged = "rooms seen: " + " ".join(sorted(seen, key=lambda r: int(r, 16)))
            bounty["evidence"] = [merged]
            if bounty["status"] == "needed":
                bounty["status"] = "partial"
            changes.append(f"{bounty['id']}: {merged}")
            continue
        if bounty["status"] == "done":
            continue
        if finding.auto:
            bounty["status"] = "done"
            bounty["evidence"] = [f"{today} ({source}): {finding.text}"]
            changes.append(f"{bounty['id']}: verified — {finding.text}")
        else:
            note = f"evidence in {source}: {finding.text} — needs review"
            if note not in evidence:
                evidence.append(note)
            if bounty["status"] == "needed":
                bounty["status"] = "partial"
            changes.append(f"{bounty['id']}: {note}")
    return changes


# ----------------------------------------------------------------------------------------------------------------
# CLI
# ----------------------------------------------------------------------------------------------------------------

def cmd_render(args) -> int:
    text = render(load_data())
    if args.check:
        current = DOC.read_text(encoding="utf-8") if DOC.exists() else ""
        if current.replace("\r\n", "\n") != text:
            print("docs/CAPTURE_BOUNTIES.md is out of date: run `python tools/bounties/bounties.py render`", file=sys.stderr)
            return 1
        print("docs/CAPTURE_BOUNTIES.md is up to date")
        return 0
    DOC.write_text(text, encoding="utf-8", newline="\n")
    print(f"wrote {DOC.relative_to(ROOT)}")
    return 0


def cmd_analyze(args) -> int:
    data = load_data()
    for path in args.captures:
        capture = read_capture(Path(path))
        findings, facts = detect(capture)
        print(report(data, capture, findings, facts))
    return 0


def cmd_apply(args) -> int:
    data = load_data()
    today = dt.date.today().isoformat()
    for path in args.captures:
        capture = read_capture(Path(path))
        findings, _ = detect(capture)
        for change in apply(data, findings, args.source or capture.name, today):
            print(change)
    save_data(data)
    DOC.write_text(render(data), encoding="utf-8", newline="\n")
    return 0


def cmd_triage(args) -> int:
    body = os.environ.get(args.body_env, "")
    urls = list(dict.fromkeys(ATTACHMENT_PATTERN.findall(body)))
    data = load_data()
    parts = []
    if not urls:
        parts.append("### Lockpick capture analysis\n\nNo capture zip was found in this issue. Drag the "
                     "`LockpickCapture-*.zip` file into the issue (edit it) and it will be analyzed automatically.\n")
    for url in urls[:5]:
        try:
            request = urllib.request.Request(url, headers={"User-Agent": "lockpick-capture-triage"})
            with urllib.request.urlopen(request, timeout=60) as response:  # noqa: S310 - URL host validated above
                payload = response.read(MAX_DOWNLOAD_BYTES + 1)
            if len(payload) > MAX_DOWNLOAD_BYTES:
                raise ValueError("zip is larger than 100 MB")
            capture = read_capture_zip(url.rsplit("/", 1)[-1], payload)
            findings, facts = detect(capture)
            parts.append(report(data, capture, findings, facts))
        except (OSError, ValueError, zipfile.BadZipFile) as error:
            parts.append(f"### Lockpick capture analysis\n\nCouldn't analyze `{url.rsplit('/', 1)[-1]}`: {error}\n")
    marker = "<!-- lockpick-capture-triage: " + " ".join(urls) + " -->"
    Path(args.out).write_text(marker + "\n" + "\n".join(parts), encoding="utf-8")
    return 0


def main(argv: list[str] | None = None) -> int:
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8")  # Windows consoles default to a legacy code page
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = parser.add_subparsers(dest="command", required=True)
    p = sub.add_parser("render")
    p.add_argument("--check", action="store_true")
    p.set_defaults(func=cmd_render)
    p = sub.add_parser("analyze")
    p.add_argument("captures", nargs="+")
    p.set_defaults(func=cmd_analyze)
    p = sub.add_parser("apply")
    p.add_argument("captures", nargs="+")
    p.add_argument("--source")
    p.set_defaults(func=cmd_apply)
    p = sub.add_parser("triage")
    p.add_argument("--body-env", required=True)
    p.add_argument("--out", required=True)
    p.set_defaults(func=cmd_triage)
    args = parser.parse_args(argv)
    return args.func(args)


if __name__ == "__main__":
    sys.exit(main())
