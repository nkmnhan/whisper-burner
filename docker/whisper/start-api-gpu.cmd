@echo off
cd /d "%~dp0"
docker compose --profile api-gpu up --build
pause
