<#
.SYNOPSIS
    Loop performance test for WhisperLive.
    Cycles through configurations, measures latency + CPU, prints a comparison table.

.USAGE
    .\scripts\test\loop-perf-test.ps1
    .\scripts\test\loop-perf-test.ps1 -WithTranslation -TranslationTarget vi
    .\scripts\test\loop-perf-test.ps1 -Chunks 3,5 -Duration 60

.NOTES
    WhisperLive must be running (release\WhisperLive.exe).
    Docker API must be up on port 5000.
    Play audio/video during the test for realistic results.
#>

param(
    [int[]]  $Chunks           = @(3, 5, 7),
    [string] $Model            = "small",
    [string] $Language         = "en",
    [int]    $Duration         = 45,
    [int]    $Rounds           = 1,
    [int]    $CoolDown         = 5,
    [switch] $WithTranslation,
    [string] $TranslationTarget   = "vi",
    [string] $TranslationProvider = "docker"
)

$root   = Split-Path $PSScriptRoot -Parent
$wl     = Join-Path $root "wl.ps1"
$logDir = Join-Path $env:USERPROFILE "whisper.live\logs"

function Invoke-Wl {
    param([string[]]$WlArgs)
    $out = & powershell -ExecutionPolicy Bypass -NoProfile -File $wl @WlArgs 2>&1
    if ($LASTEXITCODE -ne 0) { throw "wl $($WlArgs[0]) failed: $out" }
    return $out
}

function Start-CpuSampler {
    return Start-Job -ScriptBlock {
        $samples = [System.Collections.Generic.List[double]]::new()
        while ($true) {
            try {
                $val = (Get-Counter '\Processor(_Total)\% Processor Time' -SampleInterval 1 -MaxSamples 1 -ErrorAction Stop).CounterSamples[0].CookedValue
                $samples.Add([math]::Round($val, 1))
            } catch { }
            Start-Sleep -Milliseconds 500
        }
        $samples
    }
}

function Stop-CpuSampler($job) {
    Stop-Job $job -ErrorAction SilentlyContinue
    $raw = Receive-Job $job -ErrorAction SilentlyContinue
    Remove-Job $job -Force -ErrorAction SilentlyContinue
    $nums = @($raw | Where-Object { $_ -is [double] -or ($_ -match '^\d+(\.\d+)?$') } | ForEach-Object { [double]$_ })
    if ($nums.Count -eq 0) { return @{ Avg=0; Max=0; Samples=0 } }
    return @{
        Avg     = [math]::Round(($nums | Measure-Object -Average).Average, 1)
        Max     = [math]::Round(($nums | Measure-Object -Maximum).Maximum, 1)
        Samples = $nums.Count
    }
}

function Get-SessionMetrics([int]$chunkSec) {
    $logFile = Get-ChildItem $logDir -Filter "app-*.log" |
               Sort-Object LastWriteTime -Descending |
               Select-Object -First 1 -ExpandProperty FullName

    $lines = Get-Content -LiteralPath $logFile -Tail 1000

    $startIdx = -1
    for ($i = $lines.Count - 1; $i -ge 0; $i--) {
        if ($lines[$i] -match 'Recording started') { $startIdx = $i; break }
    }
    if ($startIdx -lt 0) { return $null }
    $session = $lines[$startIdx..($lines.Count - 1)]

    # Parse transcription timing lines: "[Metrics] Transcription: 1440ms  2437.6KB -> 1 seg(s)"
    $txItems = @($session | ForEach-Object {
        if ($_ -match '\[Metrics\] Transcription:\s+(\d+)ms.*?->\s*(\d+)') {
            [PSCustomObject]@{ Ms = [int]$Matches[1]; Segs = [int]$Matches[2] }
        }
    })

    # Parse translation timing lines: "[Metrics] Translation: 320ms"
    $tlItems = @($session | ForEach-Object {
        if ($_ -match '\[Metrics\] Translation:\s+(\d+)ms') {
            [int]$Matches[1]
        }
    })

    $drops = @($session | Where-Object { $_ -match 'API saturated' }).Count
    $skips = @($session | Where-Object { $_ -match 'Skipping stale' }).Count

    $n = $txItems.Count
    if ($n -eq 0) { return $null }

    $sortedMs = ($txItems | Sort-Object Ms).Ms
    $avg  = [int](($sortedMs | Measure-Object -Average).Average)
    $p95  = $sortedMs[[math]::Min($sortedMs.Count - 1, [math]::Ceiling($sortedMs.Count * 0.95) - 1)]
    $segs = ($txItems | Measure-Object Segs -Sum).Sum
    $zeros = @($txItems | Where-Object { $_.Segs -eq 0 }).Count

    $tlAvg = 0
    if ($tlItems.Count -gt 0) {
        $tlAvg = [int](($tlItems | Measure-Object -Average).Average)
    }

    return [PSCustomObject]@{
        N            = $n
        AvgMs        = $avg
        P95Ms        = $p95
        TotalSegs    = $segs
        ZeroChunks   = $zeros
        Drops        = $drops
        Skips        = $skips
        TranslationN = $tlItems.Count
        TranslationAvgMs = $tlAvg
    }
}

# ── Header ─────────────────────────────────────────────────────────────────────
Write-Host ""
Write-Host "  WhisperLive Performance Test" -ForegroundColor White
Write-Host "  ==============================" -ForegroundColor White
$txLabel = if ($WithTranslation) { "translation=ON ($TranslationTarget)" } else { "translation=OFF" }
Write-Host "  Model=$Model  Language=$Language  Duration=${Duration}s  $txLabel" -ForegroundColor DarkGray
Write-Host "  Chunks: $($Chunks -join 's, ')s" -ForegroundColor DarkGray
Write-Host ""

# Verify app is up
Write-Host "  Checking app..." -ForegroundColor Cyan
& powershell -ExecutionPolicy Bypass -NoProfile -File $wl status | Out-Null
if ($LASTEXITCODE -ne 0) {
    Write-Host "  ERROR: App not reachable. Launch release\WhisperLive.exe first." -ForegroundColor Red
    exit 1
}
Write-Host "  App is up." -ForegroundColor Green

# Configure translation setting once before loop
Write-Host "  Configuring settings (translation=$($WithTranslation.IsPresent))..." -ForegroundColor Cyan
$settingsArgs = @("settings", "-EnableTranslation", $WithTranslation.IsPresent.ToString().ToLower())
if ($WithTranslation) {
    $settingsArgs += @("-TranslationTarget", $TranslationTarget, "-TranslationProvider", $TranslationProvider)
}
& powershell -ExecutionPolicy Bypass -NoProfile -File $wl @settingsArgs | Out-Null
Write-Host "  Settings applied.`n" -ForegroundColor Green

$results = [System.Collections.Generic.List[PSCustomObject]]::new()

for ($round = 1; $round -le $Rounds; $round++) {
    if ($Rounds -gt 1) { Write-Host "  -- Round $round / $Rounds --" -ForegroundColor White }

    foreach ($chunk in $Chunks) {
        $label = "chunk=${chunk}s"
        Write-Host "  Testing $label ..." -ForegroundColor Cyan

        $cpuJob = Start-CpuSampler

        try {
            Invoke-Wl @("start", "-Model", $Model, "-Language", $Language, "-Chunk", "$chunk") | Out-Null
        } catch {
            Write-Host "    SKIP: $($_.Exception.Message)" -ForegroundColor Yellow
            Stop-CpuSampler $cpuJob | Out-Null
            continue
        }

        $elapsed = 0
        while ($elapsed -lt $Duration) {
            $wait = [math]::Min(10, $Duration - $elapsed)
            Start-Sleep -Seconds $wait
            $elapsed += $wait

            $raw = & powershell -ExecutionPolicy Bypass -NoProfile -File $wl status 2>$null
            $statusOut = $raw | ConvertFrom-Json -ErrorAction SilentlyContinue
            if ($statusOut -and $statusOut.data) {
                $d = $statusOut.data
                $rt = if ($d.latency.avgMs -gt 0) { [math]::Round($d.latency.avgMs / ($chunk * 1000), 2) } else { "?" }
                Write-Host ("    {0,3}s | chunks={1}  segs={2}  avg={3}ms  rt={4}x" -f `
                    $elapsed, $d.chunksProcessed, $d.segmentsProduced, $d.latency.avgMs, $rt) -ForegroundColor DarkGray
            }
        }

        try { Invoke-Wl @("stop") | Out-Null } catch { }
        Start-Sleep -Seconds 2

        $cpu = Stop-CpuSampler $cpuJob
        $m   = Get-SessionMetrics -chunkSec $chunk

        if (-not $m) { Write-Host "    No metrics captured." -ForegroundColor Yellow; continue }

        $rtRatio     = if ($chunk -gt 0) { [math]::Round($m.AvgMs / ($chunk * 1000), 2) } else { 0 }
        $dropRate    = if (($m.N + $m.Drops) -gt 0) { [math]::Round($m.Drops / ($m.N + $m.Drops) * 100, 1) } else { 0 }
        $emptyRate   = if ($m.N -gt 0) { [math]::Round($m.ZeroChunks / $m.N * 100, 1) } else { 0 }
        $color       = if ($rtRatio -lt 1.0) { 'Green' } elseif ($rtRatio -lt 2.0) { 'Yellow' } else { 'Red' }

        $tlLabel = if ($m.TranslationN -gt 0) { "+tl=$($m.TranslationAvgMs)ms" } else { "" }
        Write-Host ("    avg={0}ms  p95={1}ms  rt={2}x  cpu={3}%  segs={4}  drops={5} {6}" -f `
            $m.AvgMs, $m.P95Ms, $rtRatio, $cpu.Avg, $m.TotalSegs, $m.Drops, $tlLabel) -ForegroundColor $color

        $results.Add([PSCustomObject]@{
            Round            = $round
            Chunk            = $chunk
            Translation      = $WithTranslation.IsPresent
            Processed        = $m.N
            AvgMs            = $m.AvgMs
            P95Ms            = $m.P95Ms
            RtRatio          = $rtRatio
            CpuAvg           = $cpu.Avg
            CpuMax           = $cpu.Max
            TotalSegs        = $m.TotalSegs
            Drops            = $m.Drops
            DropRate         = $dropRate
            EmptyRate        = $emptyRate
            TranslationAvgMs = $m.TranslationAvgMs
        })

        if ($chunk -ne $Chunks[-1] -or $round -lt $Rounds) {
            Write-Host "    Cooling ${CoolDown}s...`n" -ForegroundColor DarkGray
            Start-Sleep -Seconds $CoolDown
        }
    }
}

# ── Results table ──────────────────────────────────────────────────────────────
Write-Host ""
Write-Host "  RESULTS" -ForegroundColor White
Write-Host ("  {0,-8} {1,-8} {2,-9} {3,-9} {4,-8} {5,-8} {6,-7} {7,-10} {8}" -f `
    "Chunk", "AvgMs", "P95Ms", "RT-Ratio", "CpuAvg%", "CpuMax%", "Drops", "TL-AvgMs", "EmptyChk%") -ForegroundColor Cyan
Write-Host ("  " + ("-" * 80)) -ForegroundColor DarkGray

foreach ($r in $results) {
    $color  = if ($r.RtRatio -lt 1.0) { 'Green' } elseif ($r.RtRatio -lt 2.0) { 'Yellow' } else { 'Red' }
    $marker = if ($r.RtRatio -lt 1.0) { "OK" } elseif ($r.RtRatio -lt 2.0) { "~" } else { "SLOW" }
    $tlCol  = if ($r.TranslationAvgMs -gt 0) { "$($r.TranslationAvgMs)ms" } else { "-" }
    Write-Host ("  {0,-8} {1,-8} {2,-9} {3,-9} {4,-8} {5,-8} {6,-7} {7,-10} {8}" -f `
        "$($r.Chunk)s", "$($r.AvgMs)ms", "$($r.P95Ms)ms",
        "$($r.RtRatio)x $marker", "$($r.CpuAvg)%", "$($r.CpuMax)%",
        $r.Drops, $tlCol, "$($r.EmptyRate)%") -ForegroundColor $color
}

Write-Host ("  " + ("-" * 80)) -ForegroundColor DarkGray
Write-Host "  RT-Ratio < 1.0x = real-time  |  TL-AvgMs = translation latency (background)" -ForegroundColor DarkGray

$best = $results | Where-Object { $_.RtRatio -lt 1.0 } | Sort-Object AvgMs | Select-Object -First 1
if ($best) {
    Write-Host ""
    Write-Host ("  Best: chunk={0}s  rt={1}x  cpu={2}%" -f $best.Chunk, $best.RtRatio, $best.CpuAvg) -ForegroundColor Green
    Write-Host "  Recommendation: set Chunk Duration to $($best.Chunk)s in Settings." -ForegroundColor Green
} else {
    $closest = $results | Sort-Object RtRatio | Select-Object -First 1
    Write-Host ""
    Write-Host ("  No config achieved real-time. Closest: chunk={0}s  rt={1}x" -f $closest.Chunk, $closest.RtRatio) -ForegroundColor Yellow
    Write-Host "  Consider: switch to 'tiny' model or use GPU." -ForegroundColor Yellow
}
Write-Host ""
