param(
    [string]$Model = "turbo",
    [string]$Language = "",
    [ValidateSet("transcribe", "translate")]
    [string]$Task = "transcribe",
    [string]$TargetLang = "",
    [string]$OutputFormat = "srt",
    [switch]$Gpu,
    [switch]$SkipBurn,
    [switch]$BurnOnly
)

$profile = if ($Gpu) { "gpu" } else { "cpu" }
$service = "whisper-$profile"

$videosDir = ".\videos"
$outputDir = ".\videos\output"

$videoExts = @(".mp4", ".mkv", ".wmv", ".avi", ".mov", ".webm",
               ".flac", ".mp3", ".wav", ".m4a", ".ogg", ".ts", ".m2ts", ".3gp")

function Invoke-Whisper([string[]]$CmdArgs) {
    docker compose --profile $profile run --rm $service @CmdArgs
}

function EscapeFilter([string]$path) {
    $path -replace "\\", "\\\\" -replace ":", "\:" -replace ",", "\,"
}

New-Item -ItemType Directory -Path $outputDir -Force | Out-Null

$videos = Get-ChildItem -LiteralPath $videosDir -File |
          Where-Object { $videoExts -contains $_.Extension.ToLower() }

if (-not $videos) {
    Write-Host "No video/audio files found in $videosDir"
    exit 1
}

$taskLabel = if ($TargetLang) { "transcribe -> $TargetLang" } else { $Task }
Write-Host "Found $($videos.Count) file(s) to process with model '$Model' (task: $taskLabel)"

$i = 0
foreach ($file in $videos) {
    $i++
    Write-Host "`n[$i/$($videos.Count)] $($file.Name)"

    $base        = [System.IO.Path]::GetFileNameWithoutExtension($file.Name)
    $srtOut      = "output/$base.srt"
    $mp4Out      = "output/$base.mp4"
    $srtPath     = Join-Path $outputDir "$base.srt"
    $mp4Path     = Join-Path $outputDir "$base.mp4"

    $burnSrtOut  = $srtOut
    $burnSrtPath = $srtPath

    if ($TargetLang) {
        $burnSrtOut  = "output/$base.$TargetLang.srt"
        $burnSrtPath = Join-Path $outputDir "$base.$TargetLang.srt"
    }

    if (-not $BurnOnly -and -not (Test-Path -LiteralPath $srtPath)) {
        $whisperArgs = @("whisper", $file.Name, "--model", $Model, "--task", $Task,
                         "--output_dir", "/app/output", "--output_format", $OutputFormat)
        if ($Language) { $whisperArgs += "--language", $Language }

        Invoke-Whisper $whisperArgs
        if ($LASTEXITCODE -ne 0) { Write-Warning "whisper failed for '$($file.Name)'"; continue }
    } elseif (-not $BurnOnly) {
        Write-Host "  SRT exists, skipping transcription"
    }

    if ($TargetLang -and (Test-Path -LiteralPath $srtPath) -and -not (Test-Path -LiteralPath $burnSrtPath)) {
        Write-Host "  Translating subtitles to '$TargetLang' -> $burnSrtOut"
        Invoke-Whisper @("python", "/opt/translate_srt.py",
                         "/app/$srtOut", "/app/$burnSrtOut", $TargetLang)
        if ($LASTEXITCODE -ne 0) { Write-Warning "translation failed for '$($file.Name)'"; continue }
    } elseif ($TargetLang -and (Test-Path -LiteralPath $burnSrtPath)) {
        Write-Host "  Translated SRT exists, skipping translation"
    }

    if ($SkipBurn -or (Test-Path -LiteralPath $mp4Path)) {
        if (-not $SkipBurn) { Write-Host "  MP4 exists, skipping burn" }
        continue
    }

    if (-not (Test-Path -LiteralPath $burnSrtPath)) {
        Write-Warning "No SRT found for '$($file.Name)', skipping burn"
        continue
    }

    Write-Host "  Burning subtitles -> $mp4Out"
    Invoke-Whisper @("ffmpeg", "-y", "-fflags", "+discardcorrupt", "-err_detect", "ignore_err",
                     "-i", $file.Name, "-vf", "subtitles='$(EscapeFilter $burnSrtOut)'", $mp4Out)

    if ($LASTEXITCODE -ne 0) { Write-Warning "ffmpeg burn failed for '$($file.Name)'" }
}

Write-Host "Done. Outputs saved to $outputDir"
