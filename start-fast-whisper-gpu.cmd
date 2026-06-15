@echo off
docker compose -f docker\fast-whisper\docker-compose.yml --profile api-gpu up --build
pause
