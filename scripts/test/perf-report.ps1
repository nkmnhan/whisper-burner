<#
.SYNOPSIS
    Parses the WhisperLive app log for the most recent recording session and
    prints a performance report: latency, throughput, segment yield, drop rate.

.USAGE
    .\scripts\test\perf-report.ps1
    .\scripts\test\perf-report.ps1 -ShowAll        # all sessions in today's log
#>

param(
    [switch]$ShowAll
)

$logDir = Join-Path $env:USERPROFILE "whisper.live\logs"
$logFile = Get-ChildItem $logDir -Filter "app-*.log" |
           Sort-Object LastWriteTime -Descending |
           Select-Object -First 1 -ExpandProperty FullName

if (-not $logFile) { Write-Error "No app log found in $logDir"; exit 1 }

$lines = Get-Content -LiteralPath $logFile

# ── Find session boundaries ─────────────────────────────────────────────────
$sessionStarts = @()
for ($i = 0; $i -lt $lines.Count; $i++) {
    if ($lines[$i] -match 'Recording started') { $sessionStarts += $i }
}

if ($sessionStarts.Count -eq 0) { Write-Host "No sessions found in log."; exit 0 }

$toProcess = if ($ShowAll) { $sessionStarts } else { @($sessionStarts[-1]) }

foreach ($startIdx in $toProcess) {
    # Collect session lines up to next session start or end of file
    $nextIdx = ($sessionStarts | Where-Object { $_ -gt $startIdx } | Select-Object -First 1)
    $sessionLines = if ($nextIdx) { $lines[$startIdx..($nextIdx - 1)] } else { $lines[$startIdx..($lines.Count-1)] }

    # ── Parse header ────────────────────────────────────────────────────────
    $header = $sessionLines[0]
    $chunkSec = if ($header -match 'chunk=(\d+)s') { [int]$Matches[1] } else { '?' }
    $model    = if ($header -match 'model=(\S+)')  { $Matches[1] }      else { '?' }
    $lang     = if ($header -match 'language=(\S+)') { $Matches[1] }    else { '?' }
    $time     = if ($header -match '^(\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2})') { $Matches[1] } else { '?' }

    # ── Collect Transcription metric lines ──────────────────────────────────
    $metrics = $sessionLines | Where-Object { $_ -match 'Transcription: (\d+)ms.*→ (\d+) seg' } | ForEach-Object {
        if ($_ -match 'Transcription: (\d+)ms\s+[\d.]+KB → (\d+) seg') {
            [PSCustomObject]@{ Ms = [int]$Matches[1]; Segs = [int]$Matches[2] }
        }
    }

    # ── Collect drop/skip warnings ───────────────────────────────────────────
    $drops   = @($sessionLines | Where-Object { $_ -match 'dropped.*API saturated' }).Count
    $skips   = @($sessionLines | Where-Object { $_ -match 'Skipping stale chunk' }).Count
    $retries = @($sessionLines | Where-Object { $_ -match 'failed — retrying' }).Count
    $errors  = @($sessionLines | Where-Object { $_ -match 'failed after retry' }).Count

    # ── Compute stats ────────────────────────────────────────────────────────
    $n        = $metrics.Count
    $totalMs  = ($metrics | Measure-Object Ms  -Sum).Sum
    $totalSeg = ($metrics | Measure-Object Segs -Sum).Sum

    if ($n -eq 0) { Write-Host "Session $time — no completed transcriptions yet."; continue }

    $avgMs  = [math]::Round($totalMs / $n)
    $sorted = ($metrics | Sort-Object Ms).Ms
    $minMs  = $sorted[0]
    $maxMs  = $sorted[-1]
    $p95Ms  = $sorted[[math]::Min($sorted.Count - 1, [math]::Ceiling($sorted.Count * 0.95) - 1)]

    $chunksFlushed = @($sessionLines | Where-Object { $_ -match 'Flushed chunk #' }).Count
    $chunksTotal   = $chunksFlushed
    $dropRate      = if ($chunksTotal -gt 0) { [math]::Round(($drops / $chunksTotal) * 100, 1) } else { 0 }
    $zeroSegChunks = @($metrics | Where-Object { $_.Segs -eq 0 }).Count
    $yieldRate     = if ($n -gt 0) { [math]::Round((($n - $zeroSegChunks) / $n) * 100, 1) } else { 0 }

    $realTimeRatio = if ($chunkSec -ne '?') { [math]::Round($avgMs / ($chunkSec * 1000), 2) } else { '?' }

    # ── Summary line ─────────────────────────────────────────────────────────
    $summaryLine = $sessionLines | Where-Object { $_ -match 'Pipeline Session Summary' } | Select-Object -Last 1

    Write-Host ""
    Write-Host "  ══════════════════════════════════════════════════════════" -ForegroundColor White
    Write-Host "  Session   : $time   model=$model  lang=$lang  chunk=${chunkSec}s" -ForegroundColor White
    Write-Host "  ══════════════════════════════════════════════════════════" -ForegroundColor White
    Write-Host ""
    Write-Host "  Latency (per chunk API call)" -ForegroundColor Cyan
    Write-Host "    avg  : ${avgMs}ms    (real-time ratio: ${realTimeRatio}x  — target <1.0x)" -ForegroundColor $(if ([double]$realTimeRatio -lt 1.0) { 'Green' } else { 'Red' })
    Write-Host "    min  : ${minMs}ms"  -ForegroundColor DarkCyan
    Write-Host "    max  : ${maxMs}ms"  -ForegroundColor DarkCyan
    Write-Host "    p95  : ${p95Ms}ms"  -ForegroundColor DarkCyan
    Write-Host ""
    Write-Host "  Throughput" -ForegroundColor Cyan
    Write-Host "    Chunks flushed   : $chunksTotal"
    Write-Host "    Chunks processed : $n"
    Write-Host "    Chunks dropped   : $drops  ($dropRate% drop rate)"  -ForegroundColor $(if ($drops -gt 0) { 'Yellow' } else { 'Green' })
    Write-Host "    Chunks skipped   : $skips"                          -ForegroundColor $(if ($skips -gt 0) { 'Yellow' } else { 'Green' })
    Write-Host ""
    Write-Host "  Segment yield" -ForegroundColor Cyan
    Write-Host "    Total segments   : $totalSeg"
    Write-Host "    Zero-seg chunks  : $zeroSegChunks / $n  ($([math]::Round(100-$yieldRate,1))% empty)" -ForegroundColor $(if ($zeroSegChunks -gt ($n/2)) { 'Red' } else { 'DarkYellow' })
    Write-Host "    Avg segs/chunk   : $([math]::Round($totalSeg / [math]::Max($n,1), 2))"
    Write-Host ""
    Write-Host "  Errors" -ForegroundColor Cyan
    Write-Host "    Retries  : $retries"  -ForegroundColor $(if ($retries -gt 0) { 'Yellow' } else { 'Green' })
    Write-Host "    Failures : $errors"   -ForegroundColor $(if ($errors  -gt 0) { 'Red'    } else { 'Green' })
    Write-Host ""
    if ($summaryLine) {
        Write-Host "  [From session-end summary in log]" -ForegroundColor DarkGray
    }
    Write-Host "  ══════════════════════════════════════════════════════════" -ForegroundColor White
}
