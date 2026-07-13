# WhisperLive Test Suite

Automated tests for the WhisperLive desktop app. All tests communicate with the running app via the named pipe at `\\.\pipe\whisper-live`.

## Prerequisites

1. **App running** — release build or debug. Start via shortcut or:
   ```powershell
   .\release\WhisperLive.exe
   ```
2. **Docker running** — Whisper API on port 5000:
   ```powershell
   .\scripts\api\start-whisper-cpu.cmd
   ```
3. **Audio playing** — functional and perf tests require audible audio for segments to be produced.

## Running Tests

### All suites (smoke → functional → perf)
```powershell
powershell -ExecutionPolicy Bypass -File tests\run-all.ps1
```

### Individual suites
```powershell
# Smoke (5 tests, ~5s, no audio needed)
powershell -ExecutionPolicy Bypass -File tests\smoke\smoke-test.ps1

# Functional (12 tests, ~90s, requires audio)
powershell -ExecutionPolicy Bypass -File tests\functional\functional-test.ps1

# Performance (60s window, requires audio)
powershell -ExecutionPolicy Bypass -File tests\perf\perf-test.ps1

# Perf with custom parameters
powershell -ExecutionPolicy Bypass -File tests\perf\perf-test.ps1 -DurationSeconds 120 -ChunkSeconds 5 -MaxRtRatio 0.5
```

### Skip slow suites
```powershell
powershell -ExecutionPolicy Bypass -File tests\run-all.ps1 -SkipFunctional -SkipPerf
```

## Exit Codes

| Code | Meaning |
|------|---------|
| `0`  | All tests passed |
| `1`  | One or more tests failed (assertions) |
| `2`  | Infrastructure error — app not reachable, abort |

## Results

JSON result files are written to `tests/results/` and gitignored. Each run creates a timestamped file:
- `tests/results/last-run.json` — smoke test
- `tests/results/perf-<timestamp>.json` — perf test

## Architecture

```
tests/
  Helpers.ps1              # Shared: Invoke-PipeCommand, Test-AppRunning, Add-TestResult
  run-all.ps1              # Orchestrator: runs all suites in order
  smoke/
    smoke-test.ps1         # 5 basic pipe health tests (~5s)
  functional/
    functional-test.ps1    # 12 lifecycle/settings/translation tests (~90s)
  perf/
    perf-test.ps1          # Latency assertions over 60s recording window
  results/
    .gitkeep               # Keeps directory tracked; result files are gitignored
```

## CLI Interface (`scripts/wl.ps1`)

Tests use the pipe CLI directly. You can also run commands manually:

```powershell
# Check status
.\scripts\wl.ps1 status

# Start recording
.\scripts\wl.ps1 start -Model small -Language en -Chunk 5

# Stop recording
.\scripts\wl.ps1 stop

# Toggle settings at runtime
.\scripts\wl.ps1 settings -EnableTranslation true -TranslationTarget vi -TranslationProvider docker
```

## Performance Thresholds (defaults)

| Metric | Threshold | Rationale |
|--------|-----------|-----------|
| RT ratio | < 0.6x | With 5s chunks, inference ≤ 3s means real-time with headroom |
| p95 latency | < 3000ms | Tails should not spike above 3s even on cold GPU |

Measured in production: **avg 1277ms, p95 1417ms, RT 0.26x** on CPU with `small` model.
