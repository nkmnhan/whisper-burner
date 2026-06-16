@echo off
setlocal
cd /d "%~dp0..\.."

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
  -p:Platform=x64 ^
  -p:WindowsAppSdkSelfContained=true

if %errorlevel% neq 0 (
    echo.
    echo  Build FAILED. See errors above.
    pause
    exit /b 1
)

:: dotnet publish -o does not copy WinUI 3 XAML resources (.xbf, .pri).
:: Discover the TFM folder dynamically so this works after a .NET upgrade.
set BUILD_BIN=
for /f "delims=" %%d in ('dir /b /ad "src\WhisperLive\bin\x64\Release" 2^>nul') do (
    if exist "src\WhisperLive\bin\x64\Release\%%d\win-x64\WhisperLive.pri" (
        set BUILD_BIN=src\WhisperLive\bin\x64\Release\%%d\win-x64
    )
)

if "%BUILD_BIN%"=="" (
    echo.
    echo  ERROR: Could not find XAML build output.
    echo  Expected: src\WhisperLive\bin\x64\Release\net*\win-x64\
    pause
    exit /b 1
)

echo  Copying XAML resources from %BUILD_BIN%...
xcopy /y /s /q "%BUILD_BIN%\*.xbf" "release\"
if %errorlevel% neq 0 (
    echo  ERROR: Failed to copy .xbf files.
    pause
    exit /b 1
)

copy /y "%BUILD_BIN%\WhisperLive.pri" "release\" > nul
if %errorlevel% neq 0 (
    echo  ERROR: Failed to copy WhisperLive.pri.
    pause
    exit /b 1
)

echo.
echo  Build succeeded.
echo  App: %~dp0..\..\release\WhisperLive.exe
echo.
echo  First time? Run scripts\app\create-shortcut.cmd to put a shortcut on your Desktop,
echo  then right-click the shortcut and choose "Pin to taskbar".
echo.
echo  Future updates: just run this file again. The shortcut keeps working.
echo.
pause