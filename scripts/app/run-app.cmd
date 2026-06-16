@echo off
echo Building...
dotnet build "%~dp0..\..\src\WhisperLive\WhisperLive.csproj" -c Debug -v quiet
if errorlevel 1 (
    echo Build failed.
    pause
    exit /b 1
)
echo Launching app...
"%~dp0..\..\src\WhisperLive\bin\Debug\net9.0-windows10.0.22621.0\WhisperLive.exe"
if errorlevel 1 (
    echo App exited with error code %errorlevel%
    pause
)
