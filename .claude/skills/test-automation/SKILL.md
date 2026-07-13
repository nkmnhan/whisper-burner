---
name: test-automation
description: >
  Use when the user says "run tests", "smoke test", "check the app", "verify the pipe",
  "test the API", "run perf test", "benchmark", "regression test", "does the app work",
  "validate my changes", "test suite", or "run all tests". Executes WhisperLive integration
  tests autonomously: smoke -> functional -> performance. Never builds the app — it must
  already be running. Always run smoke first; stop if exit code 2 (infra failure).
---

# WhisperLive Test Automation

## Architecture

The app exposes a **named-pipe JSON API** at `\\.\pipe\whisper-live`.
All tests drive the app exclusively through this pipe via `scripts/wl.ps1`.
No UI automation, no process injection, no mocking — tests run against the real app.

```
tests/
  smoke/           # <10s — pipe alive, response shape correct
  functional/      # ~2min — full start/record/stop lifecycle, settings, translation
  perf/            # 5-10min — RT-ratio, CPU, segment yield under load
  results/         # JSON output from every run (gitignored)
  run-all.ps1      # Runs smoke -> functional -> perf in order
  README.md        # Human-readable guide
```

## Prerequisites

Before running any test:
1. `release\WhisperLive.exe` must be running
2. Docker API must be up: `curl http://localhost:5000/health` returns `{"status":"ok"}`
3. Audio must be playing on the system (for transcription/perf tests to produce segments)

If `smoke-test.ps1` exits with code **2** → app is not running. Do NOT mark as test failure. Tell the user to launch the app first.

## Test Layers

| Layer | Script | Duration | What it proves |
|---|---|---|---|
| **Smoke** | `tests/smoke/smoke-test.ps1` | <10s | Pipe responds, response JSON is correctly shaped, bad commands handled gracefully |
| **Functional** | `tests/functional/functional-test.ps1` | ~2min | start/stop lifecycle, settings command, translation enable/disable, state machine |
| **Performance** | `tests/perf/perf-test.ps1` | 5-10min | RT-ratio < 1.5x, no drops in steady state, translation doesn't block transcription |
| **Full suite** | `tests/run-all.ps1` | ~15min | All of the above in sequence |

## Agent Workflow

1. Run smoke first — if exit 2, stop and say "App is not running. Launch `release\WhisperLive.exe` first."
2. If smoke passes (exit 0), run functional
3. If functional passes, run perf — but only if user asked for performance testing or if a perf regression is suspected
4. Read `tests/results/last-run.json` to get structured pass/fail per test case
5. For failures: report `name`, `expected`, `actual`, and `detail` from the JSON — do not guess
6. For infra failures (exit 2): check Docker with `curl http://localhost:5000/health` and check app process with `Get-Process -Name WhisperLive`

## Running Tests

```powershell
# Smoke only (always run this first)
powershell -ExecutionPolicy Bypass -File tests\smoke\smoke-test.ps1

# Functional tests
powershell -ExecutionPolicy Bypass -File tests\functional\functional-test.ps1

# Performance test (requires audio playing)
powershell -ExecutionPolicy Bypass -File tests\perf\perf-test.ps1

# Full suite
powershell -ExecutionPolicy Bypass -File tests\run-all.ps1

# With translation (Vietnamese)
powershell -ExecutionPolicy Bypass -File tests\run-all.ps1 -WithTranslation -TranslationTarget vi
```

## Exit Codes (ALL scripts follow this contract)

| Code | Meaning | Agent Action |
|---|---|---|
| `0` | PASS — all tests in suite passed | Report success |
| `1` | FAIL — one or more tests failed | Read `results/last-run.json`, report failed tests with expected vs actual |
| `2` | INFRA — app not running or pipe unreachable | Do not count as test failure; fix infrastructure first |

## Result File Contract

Every script writes `tests/results/last-run.json`:

```json
{
  "suite": "smoke",
  "timestamp": "2026-07-10T11:00:00Z",
  "allPassed": true,
  "durationMs": 4200,
  "tests": [
    {
      "name": "pipe-reachable",
      "passed": true,
      "durationMs": 120,
      "expected": "connection within 5000ms",
      "actual": "connected in 120ms",
      "detail": ""
    },
    {
      "name": "status-response-shape",
      "passed": false,
      "durationMs": 88,
      "expected": "{ ok: true, data: { state: string, chunksProcessed: int } }",
      "actual": "{ ok: true, data: {} }",
      "detail": "data.state field missing"
    }
  ],
  "infrastructure": {
    "appRunning": true,
    "dockerHealthy": true,
    "pipeName": "whisper-live"
  }
}
```

## Performance Pass Thresholds

| Metric | Pass | Fail |
|---|---|---|
| RT-ratio (avg latency / chunk duration) | `< 1.5x` | `>= 1.5x` |
| Chunk drop rate | `< 10%` | `>= 10%` |
| Translation avg latency | `< 3000ms` | `>= 3000ms` |
| Segments produced (60s run) | `>= 3` | `< 3` (silence or hallucination only) |

## CLI Reference (pipe commands via `scripts/wl.ps1`)

```powershell
# All commands — use these to interact with the app during tests
& scripts\wl.ps1 status                                        # get live metrics
& scripts\wl.ps1 start -Model small -Language en -Chunk 5     # start recording
& scripts\wl.ps1 stop                                          # stop recording
& scripts\wl.ps1 pause / resume                                # pause/resume
& scripts\wl.ps1 settings -EnableTranslation $true -TranslationTarget vi
& scripts\wl.ps1 settings -Model small -Chunk 3
```

## Adding New Tests

1. Copy an existing test script from `tests/smoke/` as a template
2. Use `Add-TestResult` helper — it writes to `$tests` list and sets `$allPass`
3. Catch `[System.TimeoutException]` separately → `exit 2` (infra, not test failure)
4. Always write `results/last-run.json` at the end using `Write-TestResults`
5. Follow naming: `verb-noun` for test names, kebab-case (e.g., `start-returns-ok`, `status-shows-recording`)
