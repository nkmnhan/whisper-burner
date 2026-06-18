@echo off
setlocal

:MENU
cls
echo.
echo  WhisperBurner
echo  =============
echo.
echo   Dev Tools
echo     [1] Start Claude
echo     [2] Start Copilot
echo.
echo   App
echo     [3] Run app (debug)
echo     [4] Build release
echo     [5] Create desktop shortcut
echo.
echo   Whisper API
echo     [6] Start API
echo     [7] Download model
echo.
echo   Batch Processing
echo     [8] Process videos
echo     [9] Translate subtitles
echo.
echo   Docker
echo     [10] Build Docker image
echo.
echo   [0] Exit
echo.
set /p ACTION= Select action:
echo.

if "%ACTION%"=="0" exit /b 0
if "%ACTION%"=="1" goto START_CLAUDE
if "%ACTION%"=="2" goto START_COPILOT
if "%ACTION%"=="3" goto RUN_APP
if "%ACTION%"=="4" goto BUILD_RELEASE
if "%ACTION%"=="5" goto CREATE_SHORTCUT
if "%ACTION%"=="6" goto START_API
if "%ACTION%"=="7" goto DOWNLOAD_MODEL
if "%ACTION%"=="8" goto PROCESS_VIDEOS
if "%ACTION%"=="9" goto TRANSLATE
if "%ACTION%"=="10" goto DOCKER_BUILD

echo  Invalid choice.
pause
goto MENU

:: ─────────────────────────────────────────
:RUN_APP
echo  Building and launching WhisperLive...
echo.
dotnet build "%~dp0src\WhisperLive\WhisperLive.csproj" -c Debug -v quiet
if errorlevel 1 ( echo  Build failed. & pause & goto MENU )
"%~dp0src\WhisperLive\bin\Debug\net9.0-windows10.0.22621.0\WhisperLive.exe"
pause
goto MENU

:: ─────────────────────────────────────────
:BUILD_RELEASE
call "%~dp0scripts\app\build-release.cmd"
goto MENU

:: ─────────────────────────────────────────
:CREATE_SHORTCUT
call "%~dp0scripts\app\create-shortcut.cmd"
goto MENU

:: ─────────────────────────────────────────
:START_API
set PROFILE=
echo  Device:
echo    [1] CPU  (no GPU required)
echo    [2] GPU  (requires NVIDIA + Docker NVIDIA runtime)
echo.
set /p DEV= Select (1 or 2):
if "%DEV%"=="1" set PROFILE=api-cpu
if "%DEV%"=="2" set PROFILE=api-gpu
if "%PROFILE%"=="" ( echo  Invalid choice. & pause & goto MENU )
echo.
echo  Starting Whisper API [%PROFILE%] on http://localhost:5000 ...
echo.
docker compose -f "%~dp0docker\docker-compose.yml" --profile %PROFILE% up --build -d
pause
goto MENU

:: ─────────────────────────────────────────
:DOWNLOAD_MODEL
set MODEL=
echo  Select model:
echo    [1] tiny     (~75 MB)   fastest, lowest accuracy
echo    [2] base     (~145 MB)  fast
echo    [3] small    (~465 MB)  good balance (default)
echo    [4] medium   (~1.5 GB)  higher accuracy
echo    [5] large-v3 (~3.1 GB)  best accuracy
echo    [6] turbo    (~1.5 GB)  fast + accurate
echo.
set /p MC= Select model (1-6, default 3):
if "%MC%"=="" set MC=3
if "%MC%"=="1" set MODEL=tiny
if "%MC%"=="2" set MODEL=base
if "%MC%"=="3" set MODEL=small
if "%MC%"=="4" set MODEL=medium
if "%MC%"=="5" set MODEL=large-v3
if "%MC%"=="6" set MODEL=turbo
if "%MODEL%"=="" ( echo  Invalid choice. & pause & goto MENU )
echo.
echo  Device:
echo    [1] CPU  (no GPU required)
echo    [2] GPU  (requires NVIDIA + Docker NVIDIA runtime)
echo.
set /p DC= Select (1 or 2):
if "%DC%"=="1" powershell -ExecutionPolicy Bypass -File "%~dp0scripts\api\download-models.ps1" -Model %MODEL%
if "%DC%"=="2" powershell -ExecutionPolicy Bypass -File "%~dp0scripts\api\download-models.ps1" -Model %MODEL% -Gpu
if not "%DC%"=="1" if not "%DC%"=="2" ( echo  Invalid choice. & pause & goto MENU )
pause
goto MENU

:: ─────────────────────────────────────────
:PROCESS_VIDEOS
echo  Device:
echo    [1] CPU
echo    [2] GPU
echo.
set /p DV= Select (1 or 2):
if "%DV%"=="1" powershell -ExecutionPolicy Bypass -File "%~dp0scripts\batch\process-videos.ps1"
if "%DV%"=="2" powershell -ExecutionPolicy Bypass -File "%~dp0scripts\batch\process-videos.ps1" -Gpu
if not "%DV%"=="1" if not "%DV%"=="2" ( echo  Invalid choice. & pause & goto MENU )
pause
goto MENU

:: ─────────────────────────────────────────
:TRANSLATE
echo  Common codes: en  vi  zh  ja  ko  fr  de  es  ar  th
echo.
set /p LANG= Enter target language code:
if "%LANG%"=="" ( echo  No language entered. & pause & goto MENU )
echo.
echo  Device:
echo    [1] CPU
echo    [2] GPU
echo.
set /p DT= Select (1 or 2):
if "%DT%"=="1" powershell -ExecutionPolicy Bypass -File "%~dp0scripts\batch\process-videos.ps1" -TargetLang %LANG%
if "%DT%"=="2" powershell -ExecutionPolicy Bypass -File "%~dp0scripts\batch\process-videos.ps1" -TargetLang %LANG% -Gpu
if not "%DT%"=="1" if not "%DT%"=="2" ( echo  Invalid choice. & pause & goto MENU )
pause
goto MENU

:: ─────────────────────────────────────────
:DOCKER_BUILD
echo  Building Docker image (gpu profile)...
echo.
docker compose -f "%~dp0docker\docker-compose.yml" --profile gpu build
pause
goto MENU

:: ─────────────────────────────────────────
:START_CLAUDE
echo  Opening Claude in a new terminal...
start cmd /k "SET CLAUDE_CODE_MAX_OUTPUT_TOKENS=64000 && claude --dangerously-skip-permissions"
goto MENU

:: ─────────────────────────────────────────
:START_COPILOT
echo  Opening Copilot in a new terminal...
start cmd /k "copilot -i /allow-all"
goto MENU
