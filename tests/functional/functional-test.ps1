<#
.SYNOPSIS
    Functional tests — validates the full recording lifecycle, settings command,
    and state machine transitions via the named pipe.
    Duration: ~90 seconds (includes actual recording waits).

.OUTPUTS
    Exit 0 = PASS
    Exit 1 = FAIL
    Exit 2 = INFRA (app or Docker not running)

    Writes: tests/results/last-run.json

.NOTES
    Requires Docker API on port 5000 for transcription.
    Play audio on the system during the recording window for segment tests.
#>

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. "$PSScriptRoot\..\Helpers.ps1"

$sw      = [System.Diagnostics.Stopwatch]::StartNew()
$wlArgs  = @{ Model = 'small'; Language = 'en'; Chunk = 5 }

Write-Host ''
Write-Host '  WhisperLive -- Functional Tests' -ForegroundColor White
Write-Host '  ================================' -ForegroundColor White

# ── Infra guard ────────────────────────────────────────────────────────────────
$appRunning    = Test-AppRunning
$dockerHealthy = Test-DockerHealthy
if (-not $appRunning) {
    Write-Host '  INFRA: App not running.' -ForegroundColor Red
    Write-TestResults -Suite 'functional' -DurationMs 0 -Infra @{ appRunning = $false }
    exit 2
}
if (-not $dockerHealthy) {
    Write-Host '  INFRA: Docker API not healthy on port 5000.' -ForegroundColor Red
    Write-TestResults -Suite 'functional' -DurationMs 0 -Infra @{ appRunning = $true; dockerHealthy = $false }
    exit 2
}
Write-Host '  App: running   Docker: healthy' -ForegroundColor DarkGray

# Ensure we start from Idle
$resp = Invoke-PipeCommand -Command 'status'
if ($resp.data.state -notin @('Idle', 'idle')) {
    Write-Host '  Stopping previous recording...' -ForegroundColor DarkGray
    Invoke-PipeCommand -Command 'stop' | Out-Null
    Start-Sleep -Seconds 3
}
Write-Host ''

# ══════════════════════════════════════════════════════════════════════════════
Write-Host '  [Group 1] Settings command' -ForegroundColor Cyan

# T1: settings - disable translation
$t = [System.Diagnostics.Stopwatch]::StartNew()
try {
    $resp = Invoke-PipeCommand -Command 'settings' -CmdArgs @{ enableTranslation = 'false' }
    $t.Stop()
    $passed = ($resp.ok -eq $true) -and ($resp.data.enableTranslation -eq $false)
    Add-TestResult 'settings-disable-translation' $passed $t.ElapsedMilliseconds `
        -Expected 'ok=true, enableTranslation=false' `
        -Actual   "ok=$($resp.ok) enableTranslation=$($resp.data.enableTranslation)"
} catch { $t.Stop(); Add-TestResult 'settings-disable-translation' $false $t.ElapsedMilliseconds -Actual $_.Exception.Message }

# T2: settings - change chunk duration
$t = [System.Diagnostics.Stopwatch]::StartNew()
try {
    $resp = Invoke-PipeCommand -Command 'settings' -CmdArgs @{ chunk = '5' }
    $t.Stop()
    $passed = ($resp.ok -eq $true) -and ($resp.data.chunk -eq 5)
    Add-TestResult 'settings-change-chunk' $passed $t.ElapsedMilliseconds `
        -Expected 'ok=true, chunk=5' -Actual "ok=$($resp.ok) chunk=$($resp.data.chunk)"
} catch { $t.Stop(); Add-TestResult 'settings-change-chunk' $false $t.ElapsedMilliseconds -Actual $_.Exception.Message }

# ══════════════════════════════════════════════════════════════════════════════
Write-Host ''
Write-Host '  [Group 2] Start / Status / Stop lifecycle' -ForegroundColor Cyan

# T3: start returns ok=true with Recording state
$t = [System.Diagnostics.Stopwatch]::StartNew()
try {
    $resp = Invoke-PipeCommand -Command 'start' -CmdArgs @{ model = 'small'; language = 'en'; chunk = '5' }
    $t.Stop()
    $passed = ($resp.ok -eq $true) -and ($resp.data.state -in @('Recording', 'recording'))
    Add-TestResult 'start-returns-recording' $passed $t.ElapsedMilliseconds `
        -Expected 'ok=true, state=Recording' -Actual "ok=$($resp.ok) state=$($resp.data.state)"
} catch { $t.Stop(); Add-TestResult 'start-returns-recording' $false $t.ElapsedMilliseconds -Actual $_.Exception.Message }

# T4: status during recording shows Recording
Start-Sleep -Seconds 2
$t = [System.Diagnostics.Stopwatch]::StartNew()
try {
    $resp = Invoke-PipeCommand -Command 'status'
    $t.Stop()
    $passed = $resp.data.state -in @('Recording', 'recording')
    Add-TestResult 'status-during-recording' $passed $t.ElapsedMilliseconds `
        -Expected 'state=Recording' -Actual "state=$($resp.data.state)"
} catch { $t.Stop(); Add-TestResult 'status-during-recording' $false $t.ElapsedMilliseconds -Actual $_.Exception.Message }

# T5: second start while recording returns error
$t = [System.Diagnostics.Stopwatch]::StartNew()
try {
    $resp = Invoke-PipeCommand -Command 'start' -CmdArgs @{ model = 'small' }
    $t.Stop()
    $passed = ($resp.ok -eq $false) -and (-not [string]::IsNullOrEmpty($resp.error))
    Add-TestResult 'double-start-rejected' $passed $t.ElapsedMilliseconds `
        -Expected 'ok=false with error message' -Actual "ok=$($resp.ok)"
} catch { $t.Stop(); Add-TestResult 'double-start-rejected' $false $t.ElapsedMilliseconds -Actual $_.Exception.Message }

# T6: after 30s, chunks are being processed
Write-Host '    Waiting 30s for transcription...' -ForegroundColor DarkGray
Start-Sleep -Seconds 30
$t = [System.Diagnostics.Stopwatch]::StartNew()
try {
    $resp = Invoke-PipeCommand -Command 'status'
    $t.Stop()
    $chunks = [int]$resp.data.chunksProcessed
    $passed = $chunks -ge 3
    Add-TestResult 'chunks-processed-after-30s' $passed $t.ElapsedMilliseconds `
        -Expected 'chunksProcessed >= 3' -Actual "chunksProcessed=$chunks"
} catch { $t.Stop(); Add-TestResult 'chunks-processed-after-30s' $false $t.ElapsedMilliseconds -Actual $_.Exception.Message }

# T7: average latency is within real-time bounds
$t = [System.Diagnostics.Stopwatch]::StartNew()
try {
    $resp = Invoke-PipeCommand -Command 'status'
    $t.Stop()
    $avgMs   = [int]$resp.data.latency.avgMs
    $rtRatio = if ($avgMs -gt 0) { [math]::Round($avgMs / 5000.0, 2) } else { 99 }
    $passed  = $rtRatio -lt 1.5
    Add-TestResult 'latency-within-realtime' $passed $t.ElapsedMilliseconds `
        -Expected 'RT-ratio < 1.5x (avgMs < 7500 for 5s chunks)' `
        -Actual   "avgMs=$avgMs RT-ratio=${rtRatio}x"
} catch { $t.Stop(); Add-TestResult 'latency-within-realtime' $false $t.ElapsedMilliseconds -Actual $_.Exception.Message }

# T8: pause works
$t = [System.Diagnostics.Stopwatch]::StartNew()
try {
    $resp = Invoke-PipeCommand -Command 'pause'
    $t.Stop()
    $passed = $resp.ok -eq $true
    Add-TestResult 'pause-accepted' $passed $t.ElapsedMilliseconds `
        -Expected 'ok=true' -Actual "ok=$($resp.ok)"
} catch { $t.Stop(); Add-TestResult 'pause-accepted' $false $t.ElapsedMilliseconds -Actual $_.Exception.Message }

# T9: resume works
Start-Sleep -Seconds 1
$t = [System.Diagnostics.Stopwatch]::StartNew()
try {
    $resp = Invoke-PipeCommand -Command 'resume'
    $t.Stop()
    $passed = $resp.ok -eq $true
    Add-TestResult 'resume-accepted' $passed $t.ElapsedMilliseconds `
        -Expected 'ok=true' -Actual "ok=$($resp.ok)"
} catch { $t.Stop(); Add-TestResult 'resume-accepted' $false $t.ElapsedMilliseconds -Actual $_.Exception.Message }

# T10: stop returns Idle
Start-Sleep -Seconds 1
$t = [System.Diagnostics.Stopwatch]::StartNew()
try {
    $resp = Invoke-PipeCommand -Command 'stop'
    $t.Stop()
    $passed = ($resp.ok -eq $true) -and ($resp.data.state -in @('Idle', 'idle'))
    Add-TestResult 'stop-returns-idle' $passed $t.ElapsedMilliseconds `
        -Expected 'ok=true, state=Idle' -Actual "ok=$($resp.ok) state=$($resp.data.state)"
} catch { $t.Stop(); Add-TestResult 'stop-returns-idle' $false $t.ElapsedMilliseconds -Actual $_.Exception.Message }

# ══════════════════════════════════════════════════════════════════════════════
Write-Host ''
Write-Host '  [Group 3] Translation settings lifecycle' -ForegroundColor Cyan

# T11: enable Vietnamese translation
$t = [System.Diagnostics.Stopwatch]::StartNew()
try {
    $resp = Invoke-PipeCommand -Command 'settings' -CmdArgs @{
        enableTranslation   = 'true'
        translationTarget   = 'vi'
        translationProvider = 'docker'
    }
    $t.Stop()
    $passed = ($resp.ok -eq $true) -and ($resp.data.enableTranslation -eq $true) `
              -and ($resp.data.translationTarget -eq 'vi')
    Add-TestResult 'enable-vi-translation' $passed $t.ElapsedMilliseconds `
        -Expected 'ok=true, enableTranslation=true, target=vi' `
        -Actual   "ok=$($resp.ok) en=$($resp.data.enableTranslation) target=$($resp.data.translationTarget)"
} catch { $t.Stop(); Add-TestResult 'enable-vi-translation' $false $t.ElapsedMilliseconds -Actual $_.Exception.Message }

# T12: cannot change settings while recording
$t = [System.Diagnostics.Stopwatch]::StartNew()
try {
    Invoke-PipeCommand -Command 'start' -CmdArgs @{ model = 'small'; language = 'en'; chunk = '5' } | Out-Null
    Start-Sleep -Milliseconds 500
    $resp = Invoke-PipeCommand -Command 'settings' -CmdArgs @{ chunk = '3' }
    $t.Stop()
    Invoke-PipeCommand -Command 'stop' | Out-Null
    $passed = $resp.ok -eq $false
    Add-TestResult 'settings-blocked-during-recording' $passed $t.ElapsedMilliseconds `
        -Expected 'ok=false (settings locked during recording)' -Actual "ok=$($resp.ok)"
} catch { $t.Stop(); Add-TestResult 'settings-blocked-during-recording' $false $t.ElapsedMilliseconds -Actual $_.Exception.Message }

# Reset translation off after tests
Invoke-PipeCommand -Command 'settings' -CmdArgs @{ enableTranslation = 'false' } | Out-Null

# ── Results ────────────────────────────────────────────────────────────────────
$sw.Stop()
$result = Write-TestResults -Suite 'functional' -DurationMs $sw.ElapsedMilliseconds -Infra @{
    appRunning = $true; dockerHealthy = $true; pipeName = 'whisper-live'
}

Write-Host ''
$total  = $script:TestResults.Count
$failed = @($script:TestResults | Where-Object { -not $_.passed }).Count
if ($script:AllPassed) {
    Write-Host ("  PASS -- all {0} functional tests passed in {1}s." -f $total, [math]::Round($sw.Elapsed.TotalSeconds, 1)) -ForegroundColor Green
    exit 0
} else {
    Write-Host ("  FAIL -- {0}/{1} tests failed. See tests/results/last-run.json" -f $failed, $total) -ForegroundColor Red
    exit 1
}
