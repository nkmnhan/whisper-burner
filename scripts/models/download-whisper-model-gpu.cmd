@echo off
:: Downloads the default 'small' model for openai-whisper (GPU).
:: Run this once before starting start-whisper-gpu.cmd for instant startup.
:: Usage: double-click, or pass a model name as argument (tiny/base/small/medium/large-v3/turbo).
set MODEL=%~1
if "%MODEL%"=="" set MODEL=small
powershell -ExecutionPolicy Bypass -File "%~dp0download-models.ps1" -Engine whisper -Model %MODEL% -Gpu
pause
