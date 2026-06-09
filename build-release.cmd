@echo off
setlocal
cd /d "%~dp0"

echo.
echo  WhisperLive -- Release Build
echo  ==============================
echo.

dotnet publish src\WhisperLive\WhisperLive.csproj ^
  -c Release ^
  -r win-x64 ^
  --self-contained false ^
  -o release ^
  --nologo ^
  -p:Platform=x64

if %errorlevel% neq 0 (
    echo.
    echo  Build FAILED. See errors above.
    pause
    exit /b 1
)

echo.
echo  Build succeeded.
echo  App: %~dp0release\WhisperLive.exe
echo.
echo  First time? Run create-shortcut.cmd to put a shortcut on your Desktop,
echo  then right-click the shortcut and choose "Pin to taskbar".
echo.
echo  Future updates: just run this file again. The shortcut keeps working.
echo.
pause
