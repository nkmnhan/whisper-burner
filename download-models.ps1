<#
.SYNOPSIS
    Pre-download Whisper models into the local models/ directory.

.DESCRIPTION
    Runs a one-off container using the existing image to download the model
    so the API server starts instantly instead of downloading on first request.
    Must be run AFTER the images are built (start-whisper-*.cmd or start-fast-whisper-*.cmd
    build the image on first launch, or run: docker compose -f docker\<engine>\docker-compose.yml --profile api-cpu build).

.PARAMETER Engine
    Which engine to download for: "whisper" or "fast-whisper". Default: fast-whisper.

.PARAMETER Model
    Model name to download. Default: small.
    whisper:       tiny, base, small, medium, large-v3, turbo
    fast-whisper:  tiny, base, small, medium, large-v3, turbo

.PARAMETER Gpu
    Use the GPU-variant image (required if you only built the gpu variant).

.EXAMPLE
    .\download-models.ps1
    .\download-models.ps1 -Engine whisper -Model medium
    .\download-models.ps1 -Engine fast-whisper -Model large-v3
    .\download-models.ps1 -Engine fast-whisper -Model small -Gpu
#>
param(
    [ValidateSet("whisper", "fast-whisper")]
    [string]$Engine = "fast-whisper",

    [ValidateSet("tiny", "base", "small", "medium", "large-v3", "turbo")]
    [string]$Model = "small",

    [switch]$Gpu
)

$variant  = if ($Gpu) { "gpu" } else { "cpu" }
$compose  = Join-Path $PSScriptRoot "docker\$Engine\docker-compose.yml"
$profile  = "api-$variant"

if ($Engine -eq "whisper") {
    $service = "whisper-api-$variant"
    # openai-whisper caches .pt files in ~/.cache/whisper (volume-mounted to models/whisper/)
    $pyCmd = "import whisper; print(f'Downloading {\"$Model\"}...'); whisper.load_model('$Model'); print('Done.')"
} else {
    $service = "fast-whisper-api-$variant"
    # faster-whisper downloads CTranslate2 format to MODEL_CACHE (/models → models/fast-whisper/)
    # MODEL_CACHE is already set as an env var in docker-compose.yml
    $device      = if ($Gpu) { "cuda" } else { "cpu" }
    $computeType = if ($Gpu) { "float16" } else { "int8" }
    $fwModel     = if ($Model -eq "turbo") { "large-v3-turbo" } else { $Model }
    $pyCmd = "from faster_whisper import WhisperModel; import os; print(f'Downloading {\"$fwModel\"}...'); WhisperModel('$fwModel', device='$device', compute_type='$computeType', download_root=os.environ.get(\"MODEL_CACHE\",\"/models\")); print('Done.')"
}

Write-Host "Engine  : $Engine"
Write-Host "Model   : $Model"
Write-Host "Variant : $variant"
Write-Host ""
Write-Host "Building image (skipped if already up to date)..."
docker compose -f $compose --profile $profile build
if ($LASTEXITCODE -ne 0) { Write-Error "Image build failed."; exit 1 }

Write-Host ""
Write-Host "Downloading model — this may take several minutes on first run..."
docker compose -f $compose --profile $profile run --rm --no-deps $service python -c $pyCmd

if ($LASTEXITCODE -eq 0) {
    Write-Host ""
    Write-Host "Model '$Model' saved to models\$Engine\  — API will start instantly from now on."
} else {
    Write-Error "Download failed. Check the output above."
}
