# RE1 Memory Map — GOG `ResidentEvil.exe` (USA, 1.0 hotfix4)

Build: 1,004,112 bytes, SHA-256 `205BD6FED6E080A38823958E5C3306CACC86A18FF9A2758179E8D33CAEEE4A78`,
x86, PE timestamp `0x33FD5643` (1997-08-22), image base `0x400000`, SizeOfImage `0x49B2000`,
DllCharacteristics `0` (no ASLR). All addresses are absolute virtual addresses for **this build only**.

The machine-readable source of truth is `data/games/re1/profiles/re1-gog-usa-hotfix4.json`.

Verification: **runtime** = observed behaving as described in the live game (2026-10-12 sessions) and
confirmed statically · **static** = confirmed by disassembling this executable · **unverified** = lead
from a third-party source, diagnostics only.

## Globals used by Lockpick

| Address | Type | Name | Meaning | Verification |
|---|---|---|---|---|
| `0xBE9820` | u8 | stage | Stage, 0-based (0 = STAGE1) | runtime |
| `0xBE9821` | u8 | room | Room within stage (two hex digits of `ROOMsRRp.RDT`) | runtime |
| `0xBE9822` | u8 | camera | Current camera (cut) | runtime |
| `0xBE636C` | i16 | hp | Player HP (player entity +0x88); goes negative at death | runtime |
| `0xBE6459` | u8 | maxHp | Player max HP (entity +0x175); 0 until a game starts; Chris 140, Jill 96 | runtime |
| `0xBE62E5` | u8 & 3 | character | 0 Chris, 1 Jill, 3 Rebecca (entity +0x01; bit 0x08 = alt costume) | runtime (Chris, Jill; Rebecca static) |
| `0xD22730` | u32 | gameTimer | In-game time, frames at 30/s; frozen in menus and door transitions | runtime |
| `0xD22777` | u8 | mainMenu | 1 while the title/main menu is shown | runtime |
| `0xBE41C0` | u32 | stateFlags | Main state bitfield (diagnostics only, see below) | static |
| `0xBE6318` | i32 | positionX | Player X (entity +0x34) | runtime |
| `0xBE631C` | i32 | positionY | Player Y (entity +0x38) | static (0 on flat floors) |
| `0xBE6320` | i32 | positionZ | Player Z (entity +0x3C) | runtime |
| `0xBE6358` | i16 | yaw | Facing (entity +0x74), units unconfirmed | unverified |
| `0xBE9944` + 2·i | u16 | inv0..inv7 | Inventory slot: low byte item id, high byte quantity (weapons: loaded rounds). 6 slots (Chris/Rebecca), 8 (Jill); compacts when an item is used | runtime |
| `0xBE9849` | u8 | equippedSlot | Equipped slot, 1-based (0 = none) | runtime |
| `0xD22768` | u32 | inventoryPtr | Active inventory (`0xBE9944`; `0xBE9950` for Rebecca) | runtime (Jill) |
| `0xBE98E4` + 2·i | u16 | box0..box3 | Item box entries: low byte id, high byte quantity (48 entries total; first 4 read) | runtime |
| `0xBE41E2` | u8 | enemyCount | Active enemy count | unverified (changed 2→0→2 across rooms 106/105/104 live) |

### Code anchors (checked in the live process before reading)

| Address | Bytes | Instruction | Purpose |
|---|---|---|---|
| `0x477DA4` | `A0 20 98 BE 00` | `mov al,[0xBE9820]` | room loader reads stage |
| `0x477DBB` | `A0 21 98 BE 00` | `mov al,[0xBE9821]` | room loader reads room |
| `0x462A4C` | `A0 22 98 BE 00` | `mov al,[0xBE9822]` | background loader reads camera |
| `0x4810D1` | `FF 05 30 27 D2 00` | `inc dword [0xD22730]` | frame loop increments timer |
| `0x40615E` | `66 83 3D 6C 63 BE 00 00` | `cmp word [0xBE636C],0` | death check reads HP |
| `0x401285` | `66 0F B6 35 59 64 BE 00` | `movzx si,byte [0xBE6459]` | heal clamp reads max HP |
| `0x4817B1` | `88 15 E5 62 BE 00` | `mov [0xBE62E5],dl` | new game stores character |
| `0x480B53` | `81 0D C0 41 BE 00 00 00 00 02` | `or dword [0xBE41C0],0x2000000` | game start sets a state flag |
| `0x40582E` | `A1 18 63 BE 00` | `mov eax,[0xBE6318]` | player X read |

### Key static evidence

- `0x4C44F8` holds the template `./usa/stage /room    .rdt`. The room loader at `0x477D90` indexes the
  hex table `0123456789abcdef` (`0x4C44E0`) with `stage+1`, the high and low nibble of `room`, and writes a
  `0`/`1` suffix from `stateFlags & 0x00800000` (Jill's RDT variant). This confirms stage/room semantics
  and the 0-based stage.
- `.\usa\stageS\rcSRRC.pak` (`0x4C2078`) is filled at `0x462A06` from stage, room and **camera**
  (`0xBE9822`); the install has `RC1000..RC1005.pak` for room 00, cameras 0–5.
- The timer is zeroed on New Game (`0x48090A`), restored from the save copy `0xBE9844` on load
  (`0x430695`), and the results screen divides the saved copy by `0x1A5E0` (1 h at 30/s) and `0x708` (1 min).

### Item ids observed live (behavioral identification)

| Id | Behavior observed | Likely item |
|---|---|---|
| `0x01` | Starting item, slot 1, equipped as melee | Knife |
| `0x02` | Quantity drops by 1 per shot, refilled to 15 on reload | Beretta |
| `0x0B` | Consumed by Beretta reloads; item box starts with two stacks of 15 | Handgun bullets |
| `0x41` | Consumed while HP went 46 → 96 (full heal) | First aid spray |
| `0x03` | Quantity drops by 1 per shot (7 → 0), equipped as a weapon | Shotgun |
| `0x44` | Consumed while HP rose 54 → 86 (+32, Jill) | Green herb? (BioRand id 68) |
| `0x11` | Picked up with quantity 6 | Shotgun shells? (unconfirmed) |
| `0x1F`, `0x23`, `0x26`, `0x2D` | Picked up / stored in the item box (0x23 has quantity 0: a key item?) | unknown |
| `0x05` | In the attract-mode demo inventory, quantity 5 | unknown |
| `0x0E` | In the attract-mode demo inventory, quantity 144 | unknown (ammo-like quantity) |
| `0x2F` | In the attract-mode demo inventory, quantity 2 | Ink ribbon? (id 47 in BioRand's table) |

Names are not shown in the UI until an item table is verified (0.2/0.4).

Stage/room note: walking up the main-hall stairs goes to `stage=1, room=0x03` (room 203): stage 2 = mansion 2F.

## `stateFlags` (`0xBE41C0`) observed values

| Situation | Value(s) |
|---|---|
| Fresh boot / title | `0x00000000`, `0x40000000`, `0x80000000` |
| Free movement, room 106 | `0x8011000C`, `0x8013000C` |
| Free movement, room 105 | `0x80000000` |
| Menu open/close | `0x80400000 → 0x60400000 → 0x80008000 → 0x60008000 → 0x40008000 → 0x40000000` |
| Door transition | `0x8201000C → 0x6601000C → 0x46010000 → 0x46000000 → 0x42000000 → 0x82000000` |
| Death | `0x8100000C` (`0x01000000` set when HP reached −10) |
| Jill game, main hall | `0x4080000C` (`0x00800000` = Jill RDT variant) |

Bits with support: `0x00800000` Jill RDT variant (static + live), `0x01000000` player dead (static + live),
`0x04000000` door transition (live), `0x00008000` menu (live). `0x02000000` is **not** a gameplay flag.

## Structures (leads for later milestones)

From the decomp (GPL-3.0; facts only, no code used) and the RE1 Mod SDK layout, consistent with each other:

| Address | Description | Verification |
|---|---|---|
| `0xBE62E4` | Player entity (0x180 bytes) | partially (fields above) |
| `0xBE6464` | Enemy array, 30 × 0x18C; slot active when `+0x00 & 1` | unverified |
| `0xBE9950` | Rebecca's inventory | unverified |
| `0xBE98E4` | Item box, 48 × `{id, qty}` | unverified |
| `0xBE9844` | Saved copy of the game timer | static |
| `0xBE9620` | Save block ("bio card"), 0x41C bytes | unverified |
| `0xBE98C0`… | Scenario/flag banks (see RESEARCH.md) | unverified |

Entity layout (shared player/enemy up to +0x8D): `+0x00` flags, `+0x01` id, `+0x34/38/3C` int32 X/Y/Z,
`+0x74` i16 yaw, `+0x88` i16 HP, `+0xDC` status flags (poison), `+0x175` max HP (player).
