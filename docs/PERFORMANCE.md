# Performance

Targets from the brief and current measurements. Targets are not assumed results: every number below
was measured, with the method stated.

| Target | Goal | Measured | Method |
|---|---|---|---|
| State polling rate | ~30 Hz | **29.9 Hz** | `lockpick-probe watch`, live GOG game, 119 polls |
| Poll cost (14 fields, cross-process) | small | **0.095 ms avg**, 6 ms max (first poll) | same run |
| Long-run polling stability | no failures | **18,572 polls in ~625 s attached, 0 failed, 0.043 ms avg, 1.6 ms max** | `lockpick-probe watch`, 40-min session incl. game exit/restart |
| Adapter + normalization per poll | — | **1.22 µs**, 1.2 KB allocated | BenchmarkDotNet `Re1SessionRead` (in-process memory) |
| `ReadProcessMemory` (4 bytes) | — | **0.36 µs** | `Win32ReadBenchmarks` (self process) |
| Exit check per poll | — | **0.23 µs** | `HasExitedCheck` |
| Event detection per poll | — | **22 ns** | `DetectEvents` |
| Fingerprint 1 MB executable | once per process | **0.75 ms** | `Sha256AndPeHeader` (cached afterwards) |
| State→event latency | < 50 ms | ≤ 33 ms poll interval + < 0.1 ms processing | derived from the above |
| Idle CPU (game not running) | minimal | one process scan per second | design; not yet profiled |
| Game frame-rate impact | < 2% | not yet measured | external read-only polling; measure in 0.3 with the overlay |

Benchmark machine: AMD Ryzen 9 9950X3D, Windows 11 25H2, .NET 10.0.12, BenchmarkDotNet 0.15.8 ShortRun
(3 iterations, so error bars are wide). Re-run with:

```powershell
dotnet run -c Release --project benchmarks/Lockpick.Benchmarks -- --filter "*" --job short
```

Notes:
- `Task.Delay(33 ms)` would poll at ~21 Hz because of the 15.6 ms default timer; the high-resolution
  waitable timer is why 29.9 Hz is reached (ADR-007).
- UI updates are coalesced; the UI only re-renders when game-facing state changes.
- Logging is asynchronous and only state transitions are logged at Information level (no per-poll writes).
