# Help Lockpick learn the game (capture a play session)

Lockpick only trusts what it has seen the real game do. Recorded play sessions are how new values (items, ammo,
item box, Rebecca, more rooms and game builds) get verified. Capture is built into Lockpick.

## Steps

1. Install Lockpick: **[Lockpick-win-Setup.exe](https://github.com/mscrnt/lockpick/releases/latest/download/Lockpick-win-Setup.exe)**
   (Windows SmartScreen may warn because the build isn't code-signed yet: *More info → Run anyway*).
2. Start Resident Evil normally (GOG Galaxy or the desktop shortcut — not "as administrator").
3. In Lockpick open **Debug** and click **Start capture**. The card turns red while recording.
4. Play. Resets, deaths, the title screen and game restarts are fine — recording continues.
5. Click **Stop and save**. A `LockpickCapture-*.zip` is created; **Open captures folder** shows it.
6. Send the zip to the maintainers (or attach it to a GitHub issue).

If you close Lockpick while capturing, the capture is saved automatically.

## What is recorded

- The game's own state memory: its main state block (~22 KB: player, all enemies, inventory, item box, puzzle and
  event flags, rooms) and a small system block (timer, menus), recorded 10 times per second as changes only. Because
  the whole block is kept, captures can be re-checked later for things Lockpick doesn't understand yet.
- A readable trace of the values Lockpick already knows, 30 times per second (changes only).
- The game executable's fingerprint (file name, size, SHA-256, PE header fields), so unsupported builds can be added.
- Lockpick version and OS version.

A 20-minute capture is about 1–2 MB zipped (about 7 MB for two hours), well within GitHub's attachment limit.

Not recorded: screenshots, audio, input, anything outside the game's memory, file paths, computer or user names.

Capturing is read-only — it never writes to the game's memory or files, the same way a LiveSplit autosplitter works.
If you submit runs to a leaderboard, check that leaderboard's rules for external tools.

## What to play

The **[bounty list](CAPTURE_BOUNTIES.md)** has everything still needed, by release — pick anything marked ⬜ and
mention the IDs (e.g. B102) when you send the zip. Send captures through a
[Capture submission](https://github.com/mscrnt/lockpick/issues/new?template=capture-submission.yml) issue.

## Especially useful (if it happens naturally)

- Using the item box (putting items in and taking them out)
- Playing as Rebecca
- Saving at a typewriter and loading the save from the title screen
- Many different items and weapons; speedruns cover a lot of ground quickly

There's no payment for captures or bounties, bounties are completed only by captures (never code changes), and
automated or AI-generated replies aren't accepted — see the [bounty rules](CAPTURE_BOUNTIES.md#bounty-rules). With your permission, you're credited by name in Lockpick's in-app
credits (Settings → Credits) and in [CONTRIBUTORS.md](../CONTRIBUTORS.md).
