<#
.SYNOPSIS
    Automated performance test for WhisperLive.
    Starts a recording session, waits, stops it, then prints the perf report.

.USAGE
    .\scripts\test\run-perf-test.ps1
    .\scripts\test\run-perf-test.ps1 -Duration 120 -Model small -Language en -Chunk 5

.NOTES
    WhisperLive must already be running (release\WhisperLive.exe).
#>

param(
    [int]   $Duration = 60,      # seconds to record
    [string]$Model    = "small",
    [string]$Language = "en",
    [int]   $Chunk    = 5
)

$root   = Split-Path $PSScriptRoot -Parent  # scripts/test -> scripts -> root
$wl     = Join-Path $root "wl.ps1"
$report = Join-Path $PSScriptRoot "perf-report.ps1"

function Invoke-Wl {
    param([string[]]$Args)
    & powershell -ExecutionPolicy Bypass -File $wl @Args
    if ($LASTEXITCODE -ne 0) { throw "wl.ps1 $($Args[0]) failed (exit $LASTEXITCODE)" }
}

Write-Host ""
Write-Host "  WhisperLive Performance Test" -ForegroundColor White
Write-Host "  =============================" -ForegroundColor White
Write-Host "  Model=$Model  Language=$Language  Chunk=${Chunk}s  Duration=${Duration}s" -ForegroundColor DarkGray
Write-Host ""

# ── 1. Verify app is reachable ───────────────────────────────────────────────
Write-Host "  [1/4] Checking app..." -ForegroundColor Cyan
& powershell -ExecutionPolicy Bypass -File $wl status
if ($LASTEXITCODE -ne 0) {
    Write-Host "  FAIL: App is not running. Launch release\WhisperLive.exe first." -ForegroundColor Red
    exit 1
}

# ── 2. Start recording ────────────────────────────────────────────────────────
Write-Host ""
Write-Host "  [2/4] Starting recording..." -ForegroundColor Cyan
Invoke-Wl @("start", "-Model", $Model, "-Language", $Language, "-Chunk", "$Chunk")

# ── 3. Wait ───────────────────────────────────────────────────────────────────
Write-Host ""
Write-Host "  [3/4] Recording for $Duration seconds..." -ForegroundColor Cyan
$elapsed = 0
while ($elapsed -lt $Duration) {
    $remaining = $Duration - $elapsed
    Write-Host "        ${elapsed}s / ${Duration}s  ($remaining remaining)" -NoNewline
    Start-Sleep -Seconds 10
    $elapsed += 10
    Write-Host "`r" -NoNewline
}
Write-Host "        ${Duration}s / ${Duration}s  (done)              "

# ── 4. Stop + report ─────────────────────────────────────────────────────────
Write-Host ""
Write-Host "  [4/4] Stopping and generating report..." -ForegroundColor Cyan
Invoke-Wl @("stop")

Write-Host "  Waiting 3s for final segments..." -ForegroundColor DarkGray
Start-Sleep -Seconds 3

Write-Host ""
& powershell -ExecutionPolicy Bypass -File $report
