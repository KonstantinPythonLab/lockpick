# Changelog

All notable changes to this project are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project uses
[Semantic Versioning](https://semver.org/).

## [Unreleased]

### Added
- Capture markers: press **Ctrl+Shift+M** while playing (configurable in Settings) or use **Add marker** on the Debug
  page to mark a moment, then describe it. Markers record the time, room and game time, are saved in the capture
  (`markers.jsonl`) and are listed in the bounty analysis report.
- A "buy me a coffee" link in the README and the repository's Sponsor button (optional; it doesn't buy features or
  bounties).

### Fixed
- Bounty analyzer: enemy encounters and kills are only counted once a room has settled; for about a frame after a
  door the enemy slots still hold the previous room's values, which were reported as phantom enemies and kills.

## [0.1.4] - 2026-10-13

### Added
- Captures record the game's state memory (`memory-N.bin`: the main game-state block and a system block, 10 Hz,
  keyframes plus changed bytes), so every bounty that lives in game memory can be checked from a capture — including
  values Lockpick hasn't identified yet. The bounty analyzer decodes all profile fields from it.
- Diagnostic (unverified) fields for 8 enemy slots, all event-flag banks and the full 48-entry item box; analyzer
  detectors for enemy types, kills, door locks, puzzle/scenario flags, room flags and room item pickups.

### Changed
- Field reads are grouped into a few block reads per poll instead of one read per field.

### Fixed
- The app shows its version consistently as the release version (e.g. `0.1.3`, keeping any pre-release suffix);
  the startup log and capture info no longer show the four-part `0.1.3.0`.

## [0.1.3] - 2026-10-13

### Added
- Bounty automation: `data/bounties.json` is the source of truth and `docs/CAPTURE_BOUNTIES.md` is generated;
  `tools/bounties/bounties.py` analyzes capture zips and records findings (only unambiguous ones verify a bounty
  automatically; captures with trainer writes never do); the `capture-triage` workflow comments on Capture
  submission issues with the analysis.

### Fixed
- Capture recordings stay compact: raw values are left to the trace and timer-only changes are written at most
  once per second (a 20-minute live session had produced a 64 MB recording).
- A capture started mid-session now records the current state first (a quiet session left the recording empty).
- Bounty analysis ignores changes outside gameplay (attract-mode demos, loads, title screen).

### Changed
- Bounty rules: unpaid, completed only by captures (never code changes), no automated or AI-generated replies;
  blank issues disabled so reports go through the forms.

## [0.1.2] - 2026-10-13

### Added
- Capture bounty list (docs/CAPTURE_BOUNTIES.md) covering everything to capture through 1.0.0, and a
  Capture submission issue form.
- CONTRIBUTING.md, README badges, milestones, labels, bounty issues, a public roadmap board and Discussions.
- In-app credits (Settings → Credits), generated from CONTRIBUTORS.md; bounties documented as unpaid (credit only).

### Changed
- CI actions updated (checkout v7, setup-dotnet v6, upload-artifact v7, codeql-action v4); xunit.runner.visualstudio 4.0.1.

## [0.1.1] - 2026-10-13

### Added
- Installer and automatic updates (Velopack): `Lockpick-win-Setup.exe` per-user install, update checks 20 s after start
  and every 6 hours (optional; pre-release channel optional), *Update and restart* from Settings, and a sidebar version
  label that highlights available updates.
- First-time setup: welcome and safety notes, game location with build check and file browser, update preferences;
  re-runnable from Settings.
- Debug page (replaces Diagnostics) with one-click session capture: raw field traces, recordings, events and build
  fingerprints per game attachment, zipped on stop or on exit. No personal information recorded.
- `GameLoaded` event (new game / save loaded), derived from in-game timer discontinuities and tested against two live
  recordings; replay now detects events on recorded timestamps so playback speed can't fake a load.
- Second live fixture: Jill session with death, New Game, a route through 1F/2F, healing and loading a save.
- Profile: runtime-verified inventory slots, equipped slot and inventory pointer (read-only diagnostics).
- CONTRIBUTORS.md and docs/CAPTURE.md.

### Changed
- Releases are built with Velopack and are normal (non-pre-release) GitHub releases unless the version has a suffix.

## [0.1.0] - 2026-10-12

**Foundation.** Verified live against GOG `ResidentEvil.exe` 1.0 hotfix4 in Chris and Jill games.

### Added
- Game-agnostic core: `GameSnapshot` with quality-tagged `Field<T>` values (valid / stale / unavailable / unsupported),
  typed events and an event detector, adapter contract, version profiles with validation, exact build matching and
  runtime image verification (PE header + code anchors), `GameMonitor` with process lifecycle handling, session
  recording and replay, non-blocking JSON-lines logging.
- Windows platform layer: process discovery by executable name and full path, read-only process handles,
  `ReadProcessMemory` typed reads, high-resolution waitable timer for 30 Hz polling, GOG install lookup.
- Resident Evil adapter and profile for GOG `ResidentEvil.exe` 1.0 hotfix4 (USA): stage, room, camera, HP, max HP,
  character, in-game timer, position and session state, live-verified on 2026-10-12.
- Desktop app (Avalonia 12): dashboard (connection, fingerprint, game state, event log), diagnostics (raw fields with
  verification level, poll statistics, live log, recording), settings (install path override, build check, profiles).
  Responsive compact layout for portrait windows; Lucide icons.
- `lockpick-probe` tool: `identify`, `watch`, `trace`, recording.
- Tests: mock adapter, fake process memory, RE1 adapter, profile validation, monitor lifecycle and failure handling,
  fixture playback from a live game trace, Win32 integration tests. Benchmarks with BenchmarkDotNet.
- CI (build + test + data hygiene), CodeQL, scheduled benchmarks, tag-driven releases, Dependabot, issue templates.
- Documentation: architecture with decision records, roadmap with acceptance criteria, research, compatibility,
  memory map, performance, development guide.
