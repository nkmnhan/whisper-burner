@echo off
docker compose -f docker\whisper\docker-compose.yml --profile api-cpu up --build
pause
