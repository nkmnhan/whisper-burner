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

:: dotnet publish -o does not copy WinUI 3 XAML resources (.xbf, .pri).
:: Copy them from the build output so ms-appx:/// URI resolution works at runtime.
set BUILD_BIN=src\WhisperLive\bin\x64\Release\net9.0-windows10.0.22621.0\win-x64
xcopy /y /s /q "%BUILD_BIN%\*.xbf" "release\"
copy /y "%BUILD_BIN%\WhisperLive.pri" "release\" > nul

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
