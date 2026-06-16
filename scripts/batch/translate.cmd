@echo off
echo.
echo  ==========================================
echo   Whisper Subtitle Translator
echo  ==========================================
echo.
echo  Common language codes:
echo.
echo    af  Afrikaans      ar  Arabic          bg  Bulgarian
echo    cs  Czech          da  Danish          de  German
echo    el  Greek          en  English         es  Spanish
echo    et  Estonian       fa  Persian         fi  Finnish
echo    fr  French         he  Hebrew          hi  Hindi
echo    hr  Croatian       hu  Hungarian       id  Indonesian
echo    it  Italian        ja  Japanese        ko  Korean
echo    lt  Lithuanian     lv  Latvian         ms  Malay
echo    nl  Dutch          no  Norwegian       pl  Polish
echo    pt  Portuguese     ro  Romanian        ru  Russian
echo    sk  Slovak         sl  Slovenian       sq  Albanian
echo    sr  Serbian        sv  Swedish         th  Thai
echo    tr  Turkish        uk  Ukrainian       vi  Vietnamese
echo    zh  Chinese
echo.
echo  ==========================================
echo.
set /p LANG= Enter language code:
echo.
if "%LANG%"=="" (
    echo  No language entered. Exiting.
    pause
    exit /b 1
)
echo  Translating subtitles to: %LANG%
echo.
powershell -ExecutionPolicy Bypass -File "%~dp0process-videos.ps1" -TargetLang %LANG% -Gpu
pause
