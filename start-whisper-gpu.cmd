@echo off
docker compose -f docker\whisper\docker-compose.yml --profile api-gpu up --build
pause
