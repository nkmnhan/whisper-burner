<#
.SYNOPSIS
    Shared helpers for all WhisperLive test scripts.
    Dot-source this file: . "$PSScriptRoot\..\Helpers.ps1"
#>

$script:WlScript  = Join-Path $PSScriptRoot '..\scripts\wl.ps1'
$script:ResultsDir = Join-Path $PSScriptRoot 'results'
$null = New-Item -ItemType Directory -Path $script:ResultsDir -Force

# ── Pipe helper ────────────────────────────────────────────────────────────────
function Invoke-PipeCommand {
    param(
        [string]   $Command,
        [hashtable]$CmdArgs    = @{},
        [int]      $TimeoutMs  = 6000
    )
    $request = [ordered]@{ command = $Command; args = $CmdArgs } | ConvertTo-Json -Compress
    $pipe    = [System.IO.Pipes.NamedPipeClientStream]::new(
        '.', 'whisper-live',
        [System.IO.Pipes.PipeDirection]::InOut,
        [System.IO.Pipes.PipeOptions]::Asynchronous)
    try {
        $pipe.Connect($TimeoutMs)
        $w = [System.IO.StreamWriter]::new($pipe); $w.AutoFlush = $true
        $r = [System.IO.StreamReader]::new($pipe)
        $w.WriteLine($request)
        $line = $r.ReadLine()
        if ([string]::IsNullOrWhiteSpace($line)) { throw "Empty response from server" }
        return $line | ConvertFrom-Json
    }
    finally { $pipe.Dispose() }
}

# Calls wl.ps1 and returns exit code
function Invoke-Wl {
    param([string[]]$WlArgs)
    & powershell -ExecutionPolicy Bypass -NoProfile -File $script:WlScript @WlArgs 2>&1 | Out-Null
    return $LASTEXITCODE
}

# ── Test result tracking ───────────────────────────────────────────────────────
$script:TestResults = [System.Collections.Generic.List[PSCustomObject]]::new()
$script:AllPassed   = $true

function Add-TestResult {
    param(
        [string]$Name,
        [bool]  $Passed,
        [int]   $DurationMs,
        [string]$Expected = '',
        [string]$Actual   = '',
        [string]$Detail   = ''
    )
    $script:TestResults.Add([PSCustomObject]@{
        name       = $Name
        passed     = $Passed
        durationMs = $DurationMs
        expected   = $Expected
        actual     = $Actual
        detail     = $Detail
    })
    if (-not $Passed) { $script:AllPassed = $false }

    $icon  = if ($Passed) { 'PASS' } else { 'FAIL' }
    $color = if ($Passed) { 'Green' } else { 'Red' }
    $extra = if ($Detail) { " -- $Detail" } else { '' }
    Write-Host ("  [{0}] {1}{2}" -f $icon, $Name, $extra) -ForegroundColor $color
}

function Write-TestResults {
    param([string]$Suite, [long]$DurationMs, [hashtable]$Infra = @{})

    $result = [ordered]@{
        suite          = $Suite
        timestamp      = (Get-Date -Format 'o')
        allPassed      = $script:AllPassed
        durationMs     = $DurationMs
        tests          = $script:TestResults
        infrastructure = $Infra
    }
    $outPath = Join-Path $script:ResultsDir 'last-run.json'
    $result | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $outPath -Encoding UTF8
    return $result
}

function Test-AppRunning {
    # Retry up to 3 times — pipe server briefly loops between accepting connections
    for ($i = 0; $i -lt 3; $i++) {
        try {
            $r = Invoke-PipeCommand -Command 'status' -TimeoutMs 5000
            if ($r -and $r.ok -eq $true) { return $true }
        } catch { }
        if ($i -lt 2) { Start-Sleep -Milliseconds 500 }
    }
    return $false
}

function Test-DockerHealthy {
    try {
        $r = Invoke-RestMethod -Uri 'http://localhost:5000/health' -TimeoutSec 3 -ErrorAction Stop
        return $r.status -eq 'ok'
    } catch { return $false }
}
