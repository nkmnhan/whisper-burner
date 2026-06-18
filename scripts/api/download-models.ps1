<#
.SYNOPSIS
    Pre-download a faster-whisper model so the API server starts instantly.
.EXAMPLE
    .\download-models.ps1
    .\download-models.ps1 -Model large-v3
    .\download-models.ps1 -Model small -Gpu
#>
param(
    [ValidateSet("tiny", "base", "small", "medium", "large-v3", "turbo")]
    [string]$Model = "small",

    [switch]$Gpu
)

$variant     = if ($Gpu) { "gpu" } else { "cpu" }
$compose     = Join-Path $PSScriptRoot "..\..\docker\docker-compose.yml"
$profile     = "api-$variant"
$service     = "fast-whisper-api-$variant"
$device      = if ($Gpu) { "cuda" } else { "cpu" }
$computeType = if ($Gpu) { "float16" } else { "int8" }
$fwModel     = if ($Model -eq "turbo") { "large-v3-turbo" } else { $Model }
$pyCmd       = "from faster_whisper import WhisperModel; import os; WhisperModel('$fwModel', device='$device', compute_type='$computeType', download_root=os.environ.get('MODEL_CACHE', '/models')); print('Done.')"

Write-Host "Model   : $Model"
Write-Host "Variant : $variant"
Write-Host ""
Write-Host "Building image (skipped if already up to date)..."
& docker compose -f $compose --profile $profile build
if ($LASTEXITCODE -ne 0) {
    Write-Error "Image build failed."
    exit 1
}

Write-Host ""
Write-Host "Downloading model '$Model' - this may take several minutes on first run..."
& docker compose -f $compose --profile $profile run --rm -d --no-deps $service python -c "$pyCmd"

if ($LASTEXITCODE -eq 0) {
    Write-Host ""
    Write-Host "Model '$Model' saved to models\ - API will start instantly from now on."
} else {
    Write-Error "Download failed. Check the output above."
}
