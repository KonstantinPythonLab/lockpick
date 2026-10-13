# Capture bounties

Everything Lockpick needs to see the real game do, grouped by the release that needs it. Each bounty is a short
thing to do in-game while a capture is running (**Debug → Start capture**, see [CAPTURE.md](CAPTURE.md)). One capture
can cover many bounties — just play and note which IDs you hit when you send the zip
([Capture submission](https://github.com/mscrnt/lockpick/issues/new?template=capture-submission.yml) issue).

Status: ✅ verified from a capture · 🟡 partly verified · ⬜ needed · 🛠️ needs a Lockpick feature first

Unless noted, bounties are for the supported build (GOG, USA, 1.0 hotfix4). Maintainers update this file when a
capture verifies something (and the profile/memory map with the evidence).

---

## 0.1.x — Foundation

| ID | Do this in-game | Verifies | Status |
|---|---|---|---|
| B001 | New Game as **Chris**, walk around the main hall | Character id 0, HP/max HP 140, stage/room/camera, position, timer | ✅ 2026-10-12 |
| B002 | New Game as **Jill** | Character id 1, max HP 96, Jill room-file bit | ✅ 2026-10-12 |
| B003 | Walk through several doors and camera angles | Room/camera changes, door-transition timing | ✅ 2026-10-12 |
| B004 | Take damage until you die | HP decreases, death flag, HP below zero | ✅ 2026-10-12 (Chris and Jill) |
| B005 | After dying, return to the title and start a New Game | Title flag, session gating, new-game detection | ✅ 2026-10-12 |
| B006 | Load a save from the title screen | Save-load detection (timer restores) | ✅ 2026-10-12 |
| B007 | Play the **Rebecca** section (Chris scenario) | Character id 3, Rebecca's inventory pointer `0xBE9950` | ⬜ |
| B008 | Go upstairs from the main hall | Stage 2 = mansion 2F (room 203) | ✅ 2026-10-12 |

## 0.2.0 — Core trainer (inventory, items, ammo)

| ID | Do this in-game | Verifies | Status |
|---|---|---|---|
| B101 | Pick up items, fire the Beretta, reload | Inventory slots (id + quantity), rounds per shot, reload from bullets | ✅ 2026-10-12 (Jill) |
| B102 | Put items **into** and take them **out of** the item box | Item box layout `0xBE98E4` | ⬜ |
| B103 | Pick up, fire and reload the **shotgun** | Shotgun id, shell ammo id, capacity | 🟡 picked up (id 0x03, 7 shells); not fired |
| B104 | Same for the **Colt Python / magnum** | Magnum + magnum rounds ids | ⬜ |
| B105 | Same for the **bazooka** with each round type (explosive, acid, flame) | Bazooka ids and round types | ⬜ |
| B106 | Use the **flamethrower** (Chris) | Flamethrower id and fuel | ⬜ |
| B107 | Use the **rocket launcher** (ending) | Rocket launcher id | ⬜ |
| B108 | Use each healing item: green, red+green, blue herb, mixed herbs, first aid spray | Item ids, heal amounts, herb combining | 🟡 first aid spray (full heal) seen |
| B109 | Get **poisoned** (snake/Yawn/spider) and cure it with a blue herb or serum | Status flags (player +0xDC) | ⬜ |
| B110 | Combine items (herbs; anything else combinable) | Combine results and slot changes | ⬜ |
| B111 | Pick up each **key item** (keys, crests, MO disk, emblems, etc.) | Key item ids → item name table | ⬜ |
| B112 | Use an ink ribbon to save at a typewriter | Ink ribbon id and count | ⬜ |
| B113 | Try to pick something up with a **full** inventory (Chris 6 / Jill 8 slots) | Slot limits per character | ⬜ |
| B114 | Switch equipped weapon between several weapons | Equipped-slot behavior per weapon | 🟡 knife/Beretta seen |

## 0.3.0 — Overlay and HUD

| ID | Do this in-game | Verifies | Status |
|---|---|---|---|
| B201 | Play windowed at a few window sizes and positions; alt-tab away and back | Game window tracking, focus handling | 🛠️ needs window geometry in captures |
| B202 | Play in fullscreen | Overlay behavior in fullscreen | 🛠️ |

## 0.4.0 — World database (rooms, cameras, items, enemies)

| ID | Do this in-game | Verifies | Status |
|---|---|---|---|
| B301 | Visit every reachable room in **stage 1** (mansion 1F) | Room ids, cameras per room, door connections | 🟡 106 105 104 107 108 109 113 115 116 |
| B302 | Visit every reachable room in **stage 2** (mansion 2F) | Same | 🟡 203 |
| B303 | Visit every reachable room in **stage 3** | Same; stage identity | ⬜ |
| B304 | Visit every reachable room in **stage 4** | Same; stage identity | ⬜ |
| B305 | Visit every reachable room in **stage 5** | Same; stage identity | ⬜ |
| B306 | Visit every reachable room in **stages 6 and 7** | Same; which rooms are revisits | ⬜ |
| B307 | Both scenarios start to finish (Chris and Jill) | Scenario room differences (ROOMxxx0 vs xxx1) | ⬜ |
| B310 | Encounter **zombies** (and let one get up again) | Enemy array `0xBE6464`, enemy type id, HP | 🟡 zombie damage seen; enemy array not yet read |
| B311 | Encounter **dogs (Cerberus)** and **crows** | Enemy type ids | ⬜ |
| B312 | Encounter **Hunters** and **Chimeras** | Enemy type ids | ⬜ |
| B313 | Encounter **spiders**, **wasps** and **sharks** | Enemy type ids | ⬜ |
| B314 | Boss fights: **Yawn** (both), **Plant 42**, **Neptune**, **Black Tiger**, **Tyrant** (both) | Boss ids and HP | ⬜ |

## 0.5.0 — Object highlighting and ESP

| ID | Do this in-game | Verifies | Status |
|---|---|---|---|
| B401 | Stand still in a few rooms for a few seconds per camera angle | Camera projection (world → screen) | 🛠️ needs synced screenshots in captures |
| B402 | Walk past items and doors in view of the camera | On-screen positions of objects | 🛠️ |

## 0.6.0 — Advanced trainer (flags, enemies, events)

| ID | Do this in-game | Verifies | Status |
|---|---|---|---|
| B501 | Unlock doors with keys and the lockpick | Lock flags bank `0xBE9874` | ⬜ |
| B502 | Solve puzzles (piano, statues, armor room, crests, V-Jolt, computer/MO disk) | Scenario flag banks | ⬜ |
| B503 | Kill each enemy type | Enemy death, kill flags | ⬜ |
| B504 | Push/move objects (statues, shelves, boxes) | Object state | ⬜ |

## 0.7.0 — Guidance and warnings

| ID | Do this in-game | Verifies | Status |
|---|---|---|---|
| B601 | Pick up items, leave the room and come back | Per-room item pickup flags `0xBE989C` | ⬜ |
| B602 | Trigger partner/cutscene events (Barry, Rebecca, Richard, Wesker) | Event flags and cutscene detection | ⬜ |
| B603 | Skip vs. watch cutscenes | Cutscene state | ⬜ |

## 0.8.0 — Speedrun and challenge suite

| ID | Do this in-game | Verifies | Status |
|---|---|---|---|
| B701 | Full **any%** run, Chris | Split points, final timer | ⬜ |
| B702 | Full **any%** run, Jill | Split points, final timer | ⬜ |
| B703 | Reach each **ending** and the results screen | End-of-game detection, final IGT, rank | ⬜ |
| B704 | Knife-only section | Weapon-use tracking | ⬜ |
| B705 | Resets mid-run (quit to title; restart the game) | Reset detection | 🟡 quit to title and game restart seen |

## 0.9.0 — Compatibility (other builds)

These only need a capture started while the game is open — the fingerprint alone helps, and a short play session
makes a new profile possible.

| ID | Build | Status |
|---|---|---|
| B801 | GOG in **French** or **German** | ⬜ |
| B802 | Earlier GOG English revision (before hotfix4) | ⬜ |
| B803 | GOG **Japanese** (`Biohazard.exe`) | ⬜ |
| B804 | Sourcenext/Mediakite Japanese + **Classic REbirth** | ⬜ |
| B805 | Original 1996/1997 **CD release** (US or EU) | ⬜ |
| B806 | **Director's Cut** PC (if one exists for your region) | ⬜ |
| B807 | Windows 10 (not 11), different GPUs, multi-monitor/high-DPI setups | ⬜ |

## 1.0.0 — Stable release

| ID | Do this | Verifies | Status |
|---|---|---|---|
| B901 | Full playthrough per supported build with every Lockpick feature on | Final regression pass | ⬜ |
