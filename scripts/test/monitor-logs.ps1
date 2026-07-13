<#
.SYNOPSIS
    Live monitor for WhisperLive app log + Docker API log in a single window.
    App lines are shown in Cyan; Docker lines in Yellow; warnings/errors in Red.
    Ctrl+C cleanly kills the docker process by PID — no orphaned processes.

.USAGE
    .\scripts\test\monitor-logs.ps1
#>

param(
    [string]$Container       = "whisper-fast-whisper-cpu-1",
    [int]   $AppTailLines    = 30,
    [int]   $DockerTailLines = 20
)

$logDir = Join-Path $env:USERPROFILE "whisper.live\logs"
$logFile = Get-ChildItem $logDir -Filter "app-*.log" |
           Sort-Object LastWriteTime -Descending |
           Select-Object -First 1 -ExpandProperty FullName

if (-not $logFile) { Write-Error "No app log found in $logDir"; exit 1 }

Write-Host ""
Write-Host "  WhisperLive Performance Monitor" -ForegroundColor White
Write-Host "  ================================" -ForegroundColor White
Write-Host "  App log   : $logFile" -ForegroundColor DarkGray
Write-Host "  Container : $Container" -ForegroundColor DarkGray
Write-Host "  Press Ctrl+C to stop." -ForegroundColor DarkGray
Write-Host ""

# ── Docker: redirect stdout/stderr to a temp file and track PID ────────────
# This is the only reliable way to kill the docker process cleanly on exit.
# Start-Job { docker logs --follow } leaves an orphaned docker.exe behind.
$dockerTempFile = [System.IO.Path]::GetTempFileName()

$dockerProc = Start-Process `
    -FilePath "docker" `
    -ArgumentList @("logs", $Container, "--follow", "--tail", "$DockerTailLines") `
    -RedirectStandardOutput $dockerTempFile `
    -RedirectStandardError  $dockerTempFile `
    -WindowStyle Hidden `
    -PassThru

# ── Two reader jobs (file-based reads — these stop cleanly) ────────────────
$appJob = Start-Job -ScriptBlock {
    param($f, $t) Get-Content -LiteralPath $f -Wait -Tail $t
} -ArgumentList $logFile, $AppTailLines

$dockerReaderJob = Start-Job -ScriptBlock {
    param($f) Get-Content -LiteralPath $f -Wait -Tail 0
} -ArgumentList $dockerTempFile

# ── Formatters ─────────────────────────────────────────────────────────────
function Format-AppLine($line) {
    if     ($line -match '\[ERR\]|failed after retry')           { Write-Host "  [APP] $line" -ForegroundColor Red }
    elseif ($line -match '\[WRN\].*failed|dropped|Skipping')     { Write-Host "  [APP] $line" -ForegroundColor DarkYellow }
    elseif ($line -match 'Chunk #\d+ → [1-9]')                   { Write-Host "  [APP] $line" -ForegroundColor Green }
    elseif ($line -match 'Transcription:.*ms')                   { Write-Host "  [APP] $line" -ForegroundColor Cyan }
    elseif ($line -match '\[WRN\]')                              { Write-Host "  [APP] $line" -ForegroundColor Yellow }
    else                                                          { Write-Host "  [APP] $line" -ForegroundColor DarkCyan }
}

function Format-DockerLine($line) {
    if     ($line -match 'ERROR|Exception|Traceback')            { Write-Host "  [DOC] $line" -ForegroundColor Red }
    elseif ($line -match 'POST /transcribe.*200')                { Write-Host "  [DOC] $line" -ForegroundColor Yellow }
    elseif ($line -match '\[blank\]|Cleaned')                    { Write-Host "  [DOC] $line" -ForegroundColor DarkYellow }
    elseif ($line -match 'model ready|startup complete')         { Write-Host "  [DOC] $line" -ForegroundColor Green }
    elseif ($line -match 'GET /health')                          { } # suppress health-check noise
    else                                                          { Write-Host "  [DOC] $line" -ForegroundColor DarkGray }
}

# ── Main loop ──────────────────────────────────────────────────────────────
try {
    while ($true) {
        foreach ($line in (Receive-Job $appJob          -ErrorAction SilentlyContinue)) { if ($line) { Format-AppLine    $line } }
        foreach ($line in (Receive-Job $dockerReaderJob -ErrorAction SilentlyContinue)) { if ($line) { Format-DockerLine $line } }
        Start-Sleep -Milliseconds 300
    }
} finally {
    # Kill the docker process by exact PID — no orphans.
    if ($null -ne $dockerProc -and -not $dockerProc.HasExited) {
        Stop-Process -Id $dockerProc.Id -Force -ErrorAction SilentlyContinue
        Write-Host "  Killed docker log process (PID $($dockerProc.Id))." -ForegroundColor DarkGray
    }

    Stop-Job    $appJob, $dockerReaderJob -ErrorAction SilentlyContinue
    Remove-Job  $appJob, $dockerReaderJob -Force -ErrorAction SilentlyContinue

    Remove-Item $dockerTempFile -ErrorAction SilentlyContinue

    Write-Host "  Monitor stopped cleanly." -ForegroundColor DarkGray
}
