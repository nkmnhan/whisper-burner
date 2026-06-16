@echo off
setlocal
cd /d "%~dp0..\.."

set EXE=%~dp0..\..\release\WhisperLive.exe
set SHORTCUT=%USERPROFILE%\Desktop\WhisperLive.lnk

if not exist "%EXE%" (
    echo  App not found. Run scripts\app\build-release.cmd first.
    pause
    exit /b 1
)

powershell -NoProfile -Command ^
  "$ws = New-Object -ComObject WScript.Shell; ^
   $s = $ws.CreateShortcut('%SHORTCUT%'); ^
   $s.TargetPath = '%EXE%'; ^
   $s.WorkingDirectory = '%~dp0..\..\release'; ^
   $s.IconLocation = '%EXE%,0'; ^
   $s.Description = 'WhisperLive'; ^
   $s.Save()"

echo.
echo  Shortcut created on Desktop: WhisperLive.lnk
echo.
echo  To pin to taskbar:
echo    Right-click the desktop shortcut, choose "Show more options",
echo    then "Pin to taskbar".
echo.
pause
