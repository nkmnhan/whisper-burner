@echo off
echo Building...
dotnet build "%~dp0src\WhisperBurner.WinUI\WhisperBurner.WinUI.csproj" -p:Platform=x64 -r win-x64 --no-restore -v quiet
if errorlevel 1 (
    echo Build failed.
    pause
    exit /b 1
)
echo Launching app...
"%~dp0src\WhisperBurner.WinUI\bin\x64\Debug\net10.0-windows10.0.19041.0\win-x64\WhisperBurner.WinUI.exe"
if errorlevel 1 (
    echo App exited with error code %errorlevel%
    pause
)
