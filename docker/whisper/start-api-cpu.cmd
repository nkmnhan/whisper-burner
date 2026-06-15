@echo off
cd /d "%~dp0"
docker compose --profile api-cpu up --build
pause
