<#
.SYNOPSIS
    CLI client for WhisperLive. Sends commands to the running app via named pipe.

.USAGE
    .\scripts\wl.ps1 status
    .\scripts\wl.ps1 start
    .\scripts\wl.ps1 start -Model small -Language en -Chunk 5
    .\scripts\wl.ps1 stop
    .\scripts\wl.ps1 pause
    .\scripts\wl.ps1 resume
    .\scripts\wl.ps1 settings -EnableTranslation $true -TranslationTarget vi
    .\scripts\wl.ps1 settings -Model small -Chunk 5 -EnableTranslation $false
#>

param(
    [Parameter(Position = 0, Mandatory = $true)]
    [ValidateSet("start", "stop", "pause", "resume", "status", "settings")]
    [string]$Command,

    [string]$Model,
    [string]$Language,
    [int]$Chunk = 0,

    # settings command — pass "true"/"false" as strings
    [string]$EnableTranslation    = "",
    [string]$TranslationTarget,
    [string]$TranslationProvider,

    [int]$TimeoutMs = 10000
)

$pipeName = "whisper-live"

# Build JSON request args
$argsMap = @{}
if ($Model)                                 { $argsMap["model"]               = $Model }
if ($Language)                              { $argsMap["language"]             = $Language }
if ($Chunk -gt 0)                           { $argsMap["chunk"]                = "$Chunk" }
if ($null -ne $EnableTranslation -and $EnableTranslation -ne "") { $argsMap["enableTranslation"] = $EnableTranslation.ToLower() }
if ($TranslationTarget)                     { $argsMap["translationTarget"]    = $TranslationTarget }
if ($TranslationProvider)                   { $argsMap["translationProvider"]  = $TranslationProvider }

$request = [ordered]@{ command = $Command; args = $argsMap } | ConvertTo-Json -Compress

try {
    $pipe = [System.IO.Pipes.NamedPipeClientStream]::new(
        ".", $pipeName,
        [System.IO.Pipes.PipeDirection]::InOut,
        [System.IO.Pipes.PipeOptions]::Asynchronous)

    $pipe.Connect($TimeoutMs)

    $writer = [System.IO.StreamWriter]::new($pipe)
    $writer.AutoFlush = $true
    $reader = [System.IO.StreamReader]::new($pipe)

    $writer.WriteLine($request)
    $responseLine = $reader.ReadLine()

    $response = $responseLine | ConvertFrom-Json

    if ($response.ok) {
        if ($response.data) {
            Write-Host ($response.data | ConvertTo-Json -Depth 5)
        } else {
            Write-Host "OK" -ForegroundColor Green
        }
        exit 0
    } else {
        Write-Host "Error: $($response.error)" -ForegroundColor Red
        exit 1
    }
}
catch [System.TimeoutException] {
    Write-Host "Error: WhisperLive is not running (pipe timeout after ${TimeoutMs}ms)." -ForegroundColor Red
    exit 2
}
catch {
    Write-Host "Error: $($_.Exception.Message)" -ForegroundColor Red
    exit 2
}
finally {
    if ($null -ne $pipe) { $pipe.Dispose() }
}

try {
    $pipe = [System.IO.Pipes.NamedPipeClientStream]::new(
        ".", $pipeName,
        [System.IO.Pipes.PipeDirection]::InOut,
        [System.IO.Pipes.PipeOptions]::Asynchronous)

    $pipe.Connect($TimeoutMs)

    $writer = [System.IO.StreamWriter]::new($pipe)
    $writer.AutoFlush = $true
    $reader = [System.IO.StreamReader]::new($pipe)

    $writer.WriteLine($request)
    $responseLine = $reader.ReadLine()

    $response = $responseLine | ConvertFrom-Json

    if ($response.ok) {
        if ($response.data) {
            Write-Host ($response.data | ConvertTo-Json -Depth 5)
        } else {
            Write-Host "OK" -ForegroundColor Green
        }
        exit 0
    } else {
        Write-Host "Error: $($response.error)" -ForegroundColor Red
        exit 1
    }
}
catch [System.TimeoutException] {
    Write-Host "Error: WhisperLive is not running (pipe timeout after ${TimeoutMs}ms)." -ForegroundColor Red
    exit 2
}
catch {
    Write-Host "Error: $($_.Exception.Message)" -ForegroundColor Red
    exit 2
}
finally {
    if ($null -ne $pipe) { $pipe.Dispose() }
}
