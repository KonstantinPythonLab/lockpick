# Contributing to Lockpick

Thanks for your interest! There are three good ways to help.

## 1. Capture play sessions (no coding)

The most valuable help right now. Lockpick only trusts what it has seen the real game do.

1. Install Lockpick ([Lockpick-win-Setup.exe](https://github.com/mscrnt/lockpick/releases/latest/download/Lockpick-win-Setup.exe)).
2. Pick something from the [bounty list](docs/CAPTURE_BOUNTIES.md) (or the open
   [`bounty` issues](https://github.com/mscrnt/lockpick/issues?q=is%3Aissue+is%3Aopen+label%3Abounty)) — or just play.
3. **Debug → Start capture**, play, **Stop and save**, and send the zip with a
   [Capture submission](https://github.com/mscrnt/lockpick/issues/new?template=capture-submission.yml).

Details and privacy: [docs/CAPTURE.md](docs/CAPTURE.md).

## 2. Report builds, bugs and ideas

- Unsupported game version: open an **Unsupported game build** issue with the fingerprint (Settings → Check build).
- Bugs: open a **Bug report** (the Debug page shows the log file location).
- Questions and ideas: [Discussions](https://github.com/mscrnt/lockpick/discussions).

## 3. Code

- Read [docs/DEVELOPMENT.md](docs/DEVELOPMENT.md) (build, test, verifying addresses) and
  [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) (structure and decisions).
- Work is planned on the [Lockpick Roadmap board](https://github.com/users/mscrnt/projects/8) and by milestone
  (0.2.0 → 1.0.0); acceptance criteria are in [docs/ROADMAP.md](docs/ROADMAP.md).
- Ground rules: never guess memory addresses (evidence in the profile, verified against the real game), keep
  game-specific code in the game adapter, no game files or assets in the repository, and keep
  `dotnet build -c Release` warning-free and `dotnet test` green.

With your permission, contributors are credited in [CONTRIBUTORS.md](CONTRIBUTORS.md).
