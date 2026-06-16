@echo off
docker compose -f "%~dp0..\..\docker\docker-compose.yml" --profile api-gpu up --build
pause
