@echo off
docker compose -f "%~dp0..\..\docker\fast-whisper\docker-compose.yml" --profile api-cpu up --build
pause
