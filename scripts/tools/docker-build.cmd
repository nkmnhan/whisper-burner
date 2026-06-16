@echo off
docker compose -f "%~dp0..\..\docker\whisper\docker-compose.yml" --profile gpu build
pause
