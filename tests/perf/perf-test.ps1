#Requires -Version 5.1
<#
.SYNOPSIS
    WhisperLive performance regression test.
    Measures transcription latency over a fixed window and asserts RT-ratio < threshold.

.PARAMETER DurationSeconds
    How long to record audio (default: 60).

.PARAMETER ChunkSeconds
    Audio chunk size in seconds (default: 5).

.PARAMETER MaxRtRatio
    Fail if avg latency / chunk_duration exceeds this (default: 0.6).

.PARAMETER MaxP95Ms
    Fail if p95 latency exceeds this value in ms (default: 3000).

.OUTPUTS
    Exits 0 (PASS), 1 (FAIL), or 2 (INFRA).
    Writes tests/results/perf-<timestamp>.json.
#>
param(
    [int]    $DurationSeconds = 60,
    [int]    $ChunkSeconds    = 5,
    [double] $MaxRtRatio      = 0.6,
    [int]    $MaxP95Ms        = 3000
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$RepoRoot   = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$HelpersPath = Join-Path $RepoRoot "tests\Helpers.ps1"
. $HelpersPath

$ResultsDir = Join-Path $RepoRoot "tests\results"
if (-not (Test-Path $ResultsDir)) { New-Item -Path $ResultsDir -ItemType Directory | Out-Null }

$Timestamp = Get-Date -Format 'yyyy-MM-dd_HH-mm-ss'
$ResultFile = Join-Path $ResultsDir "perf-$Timestamp.json"

$Results = @()

function Add-PerfResult {
    param([string]$Name, [bool]$Passed, [string]$Message, [hashtable]$Data = @{})
    $script:Results += @{
        name    = $Name
        passed  = $Passed
        message = $Message
        data    = $Data
    }
    $colour = if ($Passed) { 'Green' } else { 'Red' }
    $mark   = if ($Passed) { '[PASS]' } else { '[FAIL]' }
    Write-Host ("  {0} {1} -- {2}" -f $mark, $Name, $Message) -ForegroundColor $colour
}

Write-Host ""
Write-Host "WhisperLive -- Performance Test" -ForegroundColor Cyan
Write-Host "  ==============================" -ForegroundColor Cyan
Write-Host ("  Duration={0}s  Chunk={1}s  MaxRT={2}x  MaxP95={3}ms" -f $DurationSeconds, $ChunkSeconds, $MaxRtRatio, $MaxP95Ms)
Write-Host ""

# ── 0. Infrastructure check ──────────────────────────────────────────────────
if (-not (Test-AppRunning)) {
    Write-Host "[INFRA] App pipe not reachable — start the app first." -ForegroundColor Red
    exit 2
}

# ── 1. Start recording ───────────────────────────────────────────────────────
Write-Host "  Starting $DurationSeconds-second recording session..." -ForegroundColor DarkGray
$startResp = Invoke-PipeCommand -Command 'start' -CmdArgs @{
    model    = 'small'
    language = 'en'
    chunk    = "$ChunkSeconds"
}
if ($startResp.state -ne 'Recording') {
    Write-Host "[INFRA] Failed to start recording." -ForegroundColor Red
    exit 2
}

# ── 2. Wait and poll ─────────────────────────────────────────────────────────
$snapshots = @()
$pollEvery = [Math]::Min(10, $DurationSeconds / 4)
$elapsed   = 0
while ($elapsed -lt $DurationSeconds) {
    $wait = [Math]::Min($pollEvery, $DurationSeconds - $elapsed)
    Start-Sleep -Seconds $wait
    $elapsed += $wait

    $snap = Invoke-PipeCommand -Command 'status' -CmdArgs @{}
    $snapshots += $snap
    Write-Host ("    {0,3}s chunks={1} segs={2} avgMs={3}" -f $elapsed, $snap.chunksProcessed, $snap.segmentsProduced, $snap.latency.avgMs) -ForegroundColor DarkGray
}

# ── 3. Stop ──────────────────────────────────────────────────────────────────
$stopResp = Invoke-PipeCommand -Command 'stop' -CmdArgs @{}
Start-Sleep -Seconds 2

# ── 4. Pull final metrics ────────────────────────────────────────────────────
$final = $snapshots | Select-Object -Last 1

$avgMs        = [int]$final.latency.avgMs
$p95Ms        = [int]$final.latency.p95Ms
$minMs        = [int]$final.latency.minMs
$maxMs        = [int]$final.latency.maxMs
$chunks       = [int]$final.chunksProcessed
$segs         = [int]$final.segmentsProduced
$rtRatio      = if ($ChunkSeconds -gt 0 -and $avgMs -gt 0) {
                    [math]::Round($avgMs / ($ChunkSeconds * 1000), 3)
                } else { 999 }

Write-Host ""
Write-Host "  --- Results ---" -ForegroundColor Cyan
Write-Host ("    Chunks processed : {0}" -f $chunks)
Write-Host ("    Segments produced: {0}" -f $segs)
Write-Host ("    Avg latency (ms) : {0}" -f $avgMs)
Write-Host ("    p95 latency (ms) : {0}" -f $p95Ms)
Write-Host ("    RT ratio         : {0}x" -f $rtRatio)
Write-Host ""

# ── 5. Assertions ────────────────────────────────────────────────────────────
Add-PerfResult -Name 'chunks-produced' -Passed ($chunks -gt 0) `
    -Message "processed $chunks chunks in ${DurationSeconds}s" `
    -Data @{ chunks = $chunks; duration = $DurationSeconds }

Add-PerfResult -Name 'segments-produced' -Passed ($segs -gt 0) `
    -Message "produced $segs segments" `
    -Data @{ segments = $segs }

$rtOk = $rtRatio -lt $MaxRtRatio -and $rtRatio -gt 0
Add-PerfResult -Name 'rt-ratio' -Passed $rtOk `
    -Message ("avg ${avgMs}ms / ${ChunkSeconds}000ms chunk = {0}x (threshold {1}x)" -f $rtRatio, $MaxRtRatio) `
    -Data @{ avgMs = $avgMs; rtRatio = $rtRatio; threshold = $MaxRtRatio }

Add-PerfResult -Name 'p95-latency' -Passed ($p95Ms -lt $MaxP95Ms -and $p95Ms -gt 0) `
    -Message ("p95=${p95Ms}ms (threshold ${MaxP95Ms}ms)") `
    -Data @{ p95Ms = $p95Ms; threshold = $MaxP95Ms }

# ── 6. Write results ─────────────────────────────────────────────────────────
$summary = @{
    timestamp      = $Timestamp
    durationSeconds = $DurationSeconds
    chunkSeconds   = $ChunkSeconds
    results        = $Results
    metrics        = @{
        chunks    = $chunks
        segments  = $segs
        avgMs     = $avgMs
        p95Ms     = $p95Ms
        minMs     = $minMs
        maxMs     = $maxMs
        rtRatio   = $rtRatio
    }
    passed         = -not ($Results | Where-Object { -not $_.passed })
}
$summary | ConvertTo-Json -Depth 5 | Set-Content -Path $ResultFile -Encoding UTF8

Write-Host ""
$failCount = ($Results | Where-Object { -not $_.passed }).Count
if ($failCount -eq 0) {
    Write-Host "  PASS -- all performance assertions met." -ForegroundColor Green
    Write-Host ("  Results: {0}" -f $ResultFile)
    exit 0
} else {
    Write-Host ("  FAIL -- {0} assertion(s) failed." -f $failCount) -ForegroundColor Red
    Write-Host ("  Results: {0}" -f $ResultFile)
    exit 1
}
