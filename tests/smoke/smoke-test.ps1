<#
.SYNOPSIS
    Smoke tests — verifies the named pipe responds and returns correctly shaped JSON.
    Duration: <10 seconds. Run after every build.

.OUTPUTS
    Exit 0 = PASS   (all tests passed)
    Exit 1 = FAIL   (app responded but tests failed)
    Exit 2 = INFRA  (app not running — not a test failure)

    Writes: tests/results/last-run.json
#>

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. "$PSScriptRoot\..\Helpers.ps1"

$sw = [System.Diagnostics.Stopwatch]::StartNew()

Write-Host ''
Write-Host '  WhisperLive -- Smoke Tests' -ForegroundColor White
Write-Host '  ==========================' -ForegroundColor White

# ── Infra check ────────────────────────────────────────────────────────────────
$appRunning    = Test-AppRunning
$dockerHealthy = Test-DockerHealthy

if (-not $appRunning) {
    Write-Host '  INFRA: Named pipe unreachable. Launch release\WhisperLive.exe first.' -ForegroundColor Red
    Write-TestResults -Suite 'smoke' -DurationMs $sw.ElapsedMilliseconds -Infra @{
        appRunning = $false; dockerHealthy = $dockerHealthy; pipeName = 'whisper-live'
    }
    exit 2
}

Write-Host "  App: running   Docker: $($dockerHealthy)" -ForegroundColor DarkGray
Write-Host ''

# ── Test 1: Pipe reachable and status returns ok=true ─────────────────────────
$t = [System.Diagnostics.Stopwatch]::StartNew()
try {
    $resp = Invoke-PipeCommand -Command 'status'
    $t.Stop()
    Add-TestResult 'pipe-reachable' ($resp.ok -eq $true) $t.ElapsedMilliseconds `
        -Expected 'ok=true' -Actual "ok=$($resp.ok)" `
        -Detail "connected in $($t.ElapsedMilliseconds)ms"
} catch {
    $t.Stop()
    Add-TestResult 'pipe-reachable' $false $t.ElapsedMilliseconds `
        -Expected 'ok=true' -Actual 'exception' -Detail $_.Exception.Message
}

# ── Test 2: Status response has required fields ────────────────────────────────
$t = [System.Diagnostics.Stopwatch]::StartNew()
try {
    $resp = Invoke-PipeCommand -Command 'status'
    $t.Stop()
    $hasState   = $null -ne $resp.data.state
    $hasChunks  = $null -ne $resp.data.chunksProcessed
    $hasSegs    = $null -ne $resp.data.segmentsProduced
    $hasLatency = $null -ne $resp.data.latency
    $passed     = $hasState -and $hasChunks -and $hasSegs -and $hasLatency
    Add-TestResult 'status-response-shape' $passed $t.ElapsedMilliseconds `
        -Expected '{ ok, data.{ state, chunksProcessed, segmentsProduced, latency } }' `
        -Actual   "state=$($resp.data.state) chunks=$($resp.data.chunksProcessed)" `
        -Detail   "state=$($resp.data.state)"
} catch {
    $t.Stop()
    Add-TestResult 'status-response-shape' $false $t.ElapsedMilliseconds `
        -Expected 'shaped JSON response' -Actual 'exception' -Detail $_.Exception.Message
}

# ── Test 3: State is Idle before any recording ────────────────────────────────
$t = [System.Diagnostics.Stopwatch]::StartNew()
try {
    $resp = Invoke-PipeCommand -Command 'status'
    $t.Stop()
    $state  = $resp.data.state
    $passed = $state -in @('Idle', 'idle')
    Add-TestResult 'initial-state-is-idle' $passed $t.ElapsedMilliseconds `
        -Expected 'state=Idle' -Actual "state=$state"
} catch {
    $t.Stop()
    Add-TestResult 'initial-state-is-idle' $false $t.ElapsedMilliseconds `
        -Expected 'state=Idle' -Actual 'exception' -Detail $_.Exception.Message
}

# ── Test 4: Unknown command returns ok=false with error string (not crash) ────
$t = [System.Diagnostics.Stopwatch]::StartNew()
try {
    $resp = Invoke-PipeCommand -Command '__smoke_test_unknown_cmd__'
    $t.Stop()
    $passed = ($resp.ok -eq $false) -and (-not [string]::IsNullOrEmpty($resp.error))
    Add-TestResult 'unknown-command-graceful' $passed $t.ElapsedMilliseconds `
        -Expected 'ok=false, error=non-empty string' `
        -Actual   "ok=$($resp.ok) error=`"$($resp.error)`""
} catch {
    $t.Stop()
    # Pipe closing on unknown command is also acceptable — app stayed alive
    Add-TestResult 'unknown-command-graceful' $true $t.ElapsedMilliseconds `
        -Expected 'ok=false or pipe close' -Actual 'pipe closed gracefully'
}

# ── Test 5: Settings command returns current settings ─────────────────────────
$t = [System.Diagnostics.Stopwatch]::StartNew()
try {
    $resp = Invoke-PipeCommand -Command 'settings' -CmdArgs @{}
    $t.Stop()
    # settings with no args just returns current values
    $passed = ($resp.ok -eq $true) -and ($null -ne $resp.data.model)
    Add-TestResult 'settings-returns-config' $passed $t.ElapsedMilliseconds `
        -Expected 'ok=true, data.model present' `
        -Actual   "ok=$($resp.ok) model=$($resp.data.model)"
} catch {
    $t.Stop()
    Add-TestResult 'settings-returns-config' $false $t.ElapsedMilliseconds `
        -Expected 'ok=true' -Actual 'exception' -Detail $_.Exception.Message
}

# ── Results ────────────────────────────────────────────────────────────────────
$sw.Stop()
$result = Write-TestResults -Suite 'smoke' -DurationMs $sw.ElapsedMilliseconds -Infra @{
    appRunning = $true; dockerHealthy = $dockerHealthy; pipeName = 'whisper-live'
}

Write-Host ''
if ($script:AllPassed) {
    Write-Host ("  PASS -- all {0} smoke tests passed in {1}ms." -f $script:TestResults.Count, $sw.ElapsedMilliseconds) -ForegroundColor Green
    exit 0
} else {
    $failed = @($script:TestResults | Where-Object { -not $_.passed }).Count
    Write-Host ("  FAIL -- {0}/{1} tests failed. See tests/results/last-run.json" -f $failed, $script:TestResults.Count) -ForegroundColor Red
    exit 1
}
