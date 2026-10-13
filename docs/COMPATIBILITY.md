# Compatibility

Lockpick reads memory only from builds it can identify **exactly**. Anything else is reported as
unsupported and never opened.

## Supported

| Game | Store / build | Executable | Size | SHA-256 | Profile | Status |
|---|---|---|---|---|---|---|
| Resident Evil (1996) | GOG, USA data, "1.0 hotfix4" (GOG build 59172465945261369) | `ResidentEvil.exe` | 1,004,112 | `205BD6FE…CAEEE4A78` | `re1-gog-usa-hotfix4` | Live-verified (Chris); Jill/Rebecca pending |

## Known other builds (not supported yet)

| Build | How to recognize | Notes |
|---|---|---|
| Earlier GOG English revision | SizeOfImage `0x49B3000` (from the RE1 autosplitter) | Addresses differ slightly; needs its own profile |
| GOG Japanese (`Biohazard.exe`) | ModuleMemorySize 77144064 | Different address set (JP) |
| Sourcenext/Mediakite JP + Classic REbirth (`Bio.exe`/`Biohazard.exe`) | ModuleMemorySize 10027008 | What BioRand, the Mod SDK and REbirth mods target |
| Original 1996/1997 CD releases (US/EU), Director's Cut PC | — | Not researched |
| HD Remaster (2015), REmake | — | Different games; out of scope |

To request support, run `lockpick-probe identify "<path to exe>"` and open an "Unsupported game build" issue.

## Environment notes

- **Privileges:** Lockpick must run at the same privilege level as the game. If the game was started
  elevated (e.g. by the GOG installer's "Launch" button), restart the game normally or run Lockpick as
  administrator. Lockpick itself never requests elevation.
- **GOG DirectX wrapper:** windowed size is controlled by `dxcfg.ini` (`display=WIDTHxHEIGHT@HZ`,
  `presentation=windowed`, `aspect=enabled` keeps 4:3). Example for a 16:9 window on an ultrawide:
  `display=1920x1080@60`. Lockpick does not edit game files.
- **Focus:** the game stops rendering when it is not the active window (original behavior).
- **Multiple monitors / DPI:** Lockpick is per-monitor DPI aware and has a compact layout for narrow or
  portrait windows (< 760 px wide).
- **Classic REbirth:** not applicable to GOG; would change the executable and every address.

## Platform

Windows 10 (1803+) or 11, x64. The app is 64-bit and reads the 32-bit game through WOW64.
High-resolution polling uses `CREATE_WAITABLE_TIMER_HIGH_RESOLUTION` (falls back to the default timer).
