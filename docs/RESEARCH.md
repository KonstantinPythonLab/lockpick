# Research Notes

Findings from the reference projects and from analysis of the GOG executable. Each item is marked
**Confirmed** (checked against the GOG build statically and/or live), **Hypothesis** (sourced but not yet
verified on this build), or **Refuted** (contradicted by observation).

Research date: 2026-10-12. No third-party code was copied into Lockpick.

## Sources and licenses

| Project | What it is | Target build | License | Use in Lockpick |
|---|---|---|---|---|
| [ecruells/resident-evil-pc-decomp](https://github.com/ecruells/resident-evil-pc-decomp) | Decompilation of the USA PC release | **GOG USA `ResidentEvil.exe` — the same build** (its globals are referenced by this exe's code) | GPL-3.0 | Addresses/offsets used as facts with credit; no code |
| [Gemini-Loboto3/RE1-Mod-SDK](https://github.com/Gemini-Loboto3/RE1-Mod-SDK) | Mod SDK for Classic REbirth | Classic REbirth `Bio.exe` (Japanese-based); 0 references in the GOG exe | CC BY-NC-ND 4.0 | Struct layouts cross-checked only |
| [shadwork/Windows-ResidentEvil1-MODS](https://github.com/shadwork/Windows-ResidentEvil1-MODS) | REbirth mods (HUD, health bars, door skip) | REbirth `Bio.exe` | MIT | Layout cross-check only |
| [deserteagle417/RE1-Autosplitter](https://github.com/deserteagle417/RE1-Autosplitter) | LiveSplit ASL | JP REbirth, JP GOG, English GOG (earlier revision) | none (all rights reserved) | Addresses used as leads with credit; no code |
| [biorand/classic](https://github.com/biorand/classic) | Randomizer for RE1–3 | RE1: Sourcenext/Mediakite JP exe + Classic REbirth | MIT | RDT format notes, item/enemy ids (future, with attribution) |

## 1. Identifying the running game

- **Confirmed:** GOG installs register under `HKLM\SOFTWARE\WOW6432Node\GOG.com\Games\1580232252`
  (`path`, `exe`, `ver` = "1.0 hotfix4"). Executable `ResidentEvil.exe`, 32-bit, no ASLR.
- **Confirmed:** The autosplitter identifies builds by `ModuleMemorySize` (EnglishGOG = 77279232 /
  `0x49B3000`). This GOG revision's SizeOfImage is `0x49B2000`, so that check fails on it — an earlier
  GOG revision existed. Lockpick identifies by SHA-256 instead.
- **Confirmed:** GOG ships its own DirectDraw wrapper (`ddraw.dll`, "GOG.com DirectX 1-7 wrapper"
  v1.1.0.5574, configured by `dxcfg.ini`) and input wrapper (`dinput.dll`). Classic REbirth is not present.
- **Confirmed:** When the GOG installer launches the game it runs elevated; a non-elevated Lockpick then
  gets `ERROR_ACCESS_DENIED` for `PROCESS_VM_READ`. Lockpick reports this explicitly.
- **Confirmed:** The game is single-instance; a second launch exits immediately.

## 2. Accessing memory

- **Confirmed:** All relevant state is in static globals of the main image (no pointer chains needed for
  0.1). Absolute addresses are stable because the image is not relocatable.
- Autosplitter offsets are module-relative (`0x400000 +`). The decomp lists absolute addresses.

## 3. Player and enemy structures

- **Confirmed (live):** HP i16 at player+0x88 (`0xBE636C`), max HP u8 at +0x175, position int32 at
  +0x34/+0x3C, character at +0x01 (Chris = 0).
- **Confirmed (live):** Jill = 1 with max HP 96 (decomp: `maxHealth = (id & 1) * -44 + 140`); a Jill game sets
  `stateFlags & 0x00800000`. **Hypothesis:** Rebecca = 3.
- **Hypothesis:** Enemy array of 30 × 0x18C at `0xBE6464` (decomp; the Mod SDK declares 32 but the USA
  code clamps to 30). The autosplitter's EnglishGOG enemy addresses are JP values copied unchanged and are
  wrong for GOG (no code references).

## 4. Room and camera

- **Confirmed (static + live):** stage/room/camera bytes at `0xBE9820/21/22`, derived from the RDT and
  background filename builders (see MEMORY_MAP.md). Room files: `USA\StageN\ROOMsRRp.RDT`, `p` = 0 Chris / 1 Jill.
- **Confirmed (live):** at a door the room id changes first; the camera id keeps the previous room's
  value for several seconds (4.7 s at 106→105; ~17 s at 105→104 during the Kenneth cutscene) while the
  game timer is frozen.

## 5. RDT files (for 0.4)

From biorand / biohazard-utils (MIT), not yet verified against the USA files:
header bytes `nSprite, nCut, nOmodel, nItem, nDoor, nRoom_at`; 19 int32 offsets at `0x48`
(cameras, collision, models, init/main/event SCD, messages, …); camera array at `0x94`.
SCD opcodes: `0x0C door_aot_set` (26 bytes; target = stage<<5 | room), `0x18 item_aot_set` (26 bytes),
`0x1B` enemy set (22 bytes). Some stage-1 rooms are read from stage+5 (`MissingRooms`).

## 6. Overlays / HUD

- REbirth mods draw in-process through the REbirth SDK. Not applicable to GOG. The GOG wrapper renders via
  Direct3D 9; an external transparent window is the planned first approach (0.3).
- **Confirmed (user report):** the game stops rendering (black) when it loses focus — a property of the
  original window procedure; fixing it needs an in-process patch.

## 7. Gameplay events

- **Refuted:** "`stateFlags & 0x02000000` = gameplay active" (decomp `g_main_state_flags`). Live: set only
  for single polls at New Game and during door transitions; clear during all free movement.
- **Confirmed (live):** `mainMenu` (`0xD22777`) is 1 at the title menu (also after dying) and 0 in game
  (autosplitter uses it for reset).
- **Confirmed (live):** `stateFlags & 0x01000000` set at death; HP overshoots below zero (−10).
- **Confirmed (live):** after death, choosing New Game keeps the dead game's room/HP in memory through
  character select and the intro (~23 s) until the new game initializes.
- **Confirmed (live):** loading a save from the title: `mainMenu` 1 → 0 on entering the load screen, then the
  timer jumps to the save's value (backwards, `0x64AE → 0x6253`), the position resets to the saved spot and the
  timer resumes. During the load-file screen (~7 s) the previous game's values remain in memory with
  `mainMenu == 0`, so they briefly count as "in game".
- **Confirmed (live, 2026-10-13):** attract mode (title screen left idle) replays the same demo each cycle on this
  build: Chris in room 107 with a preset 8-slot inventory and item box, `mainMenu = 0`, max HP 140 but **HP 0**.
  The session rule (`hp > 0`) therefore treats demos as "not in game", so they produce no state or events.
- **Confirmed (live, 2026-10-13):** in **Jill's** game, saving at a typewriter on the GOG PC build consumed **no ink
  ribbons** (owner saved three times with no ribbon in the inventory and no quantity change; `SAVE\savedat1–3.dat`
  were written at the save times). **Chris's** game does use ink ribbons (owner) — ribbon use depends on the
  scenario, not the platform. During a save the in-game timer freezes (~7 s) and no profile field or state-flag bit changes,
  so saves are detected from the save files, not memory.
- **Refuted for this build:** the autosplitter's `time` (`0x4D46D4`) is not the live timer — it is a
  copy written at one code site (`0x4298AB`). The live timer is `0xD22730`.

## 8. Pickups and interactables

- **Confirmed (static):** inventory at `0xBE9944`, 2 bytes per slot (`mov byte [eax+0xBE9944], id` /
  `mov byte [eax+0xBE9945], 1`). Item ids (biorand, matches autosplitter lists): 0 none, 1 knife,
  2 Beretta, 3 shotgun, … 47 ink ribbon, 61 small key, 66 serum, 67–75 herbs, 0x6F Uzi, 0x70 minigun.
- Room items come from RDT `item_aot_set`; per-room pickup flags live in a flag bank at `0xBE989C` (hypothesis).

## Methods

- Static analysis: Python + Capstone linear sweep over `.text`, searching absolute operands, plus string
  cross-references (`./usa/stage /room    .rdt`, `rcSRRC.pak`, `savedat%d.dat`). Scripts were run on the
  local install only; nothing from the game is committed.
- Runtime: `lockpick-probe trace` (read-only) during normal play. Raw traces are kept next to fixtures.
