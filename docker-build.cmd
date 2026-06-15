@echo off
docker compose -f docker\whisper\docker-compose.yml --profile gpu build
pause
