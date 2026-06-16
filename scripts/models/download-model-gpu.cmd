@echo off
set MODEL=%~1
if "%MODEL%"=="" set MODEL=small
powershell -ExecutionPolicy Bypass -File "%~dp0download-models.ps1" -Model %MODEL% -Gpu
pause
