@echo off
powershell -ExecutionPolicy Bypass -File "%~dp0process-videos.ps1" -Gpu
pause
