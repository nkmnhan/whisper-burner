#Requires -Version 5.1
<#
.SYNOPSIS
    Runs all WhisperLive test suites: smoke → functional → perf.
    Stops at first infrastructure failure (exit 2).

.OUTPUTS
    Exits 0 (all passed), 1 (one or more suites failed).
#>
param(
    [switch]$SkipFunctional,
    [switch]$SkipPerf
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$TestsRoot = $PSScriptRoot
$Failed    = $false

function Invoke-Suite {
    param([string]$Label, [string]$Script)

    Write-Host ""
    Write-Host ("=== {0} ===" -f $Label) -ForegroundColor Cyan
    & powershell -ExecutionPolicy Bypass -NoProfile -File $Script
    $code = $LASTEXITCODE

    if ($code -eq 2) {
        Write-Host "  INFRA ERROR — aborting run." -ForegroundColor Red
        exit 2
    }
    if ($code -ne 0) {
        $script:Failed = $true
    }
}

Write-Host ""
Write-Host "WhisperLive -- Full Test Run" -ForegroundColor Yellow
Write-Host "============================" -ForegroundColor Yellow

# Smoke
Invoke-Suite -Label "Smoke" -Script (Join-Path $TestsRoot "smoke\smoke-test.ps1")

# Functional
if (-not $SkipFunctional) {
    Invoke-Suite -Label "Functional" -Script (Join-Path $TestsRoot "functional\functional-test.ps1")
}

# Perf
if (-not $SkipPerf) {
    Invoke-Suite -Label "Performance" -Script (Join-Path $TestsRoot "perf\perf-test.ps1")
}

Write-Host ""
if ($Failed) {
    Write-Host "OVERALL: FAIL -- one or more suites had failures." -ForegroundColor Red
    exit 1
} else {
    Write-Host "OVERALL: PASS" -ForegroundColor Green
    exit 0
}
