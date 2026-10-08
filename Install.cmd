@echo off
setlocal
"%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -ExecutionPolicy Bypass -File "%~dp0Setup.ps1"
if errorlevel 1 (
    echo Installation did not finish. See the message above.
    pause
    exit /b 1
)
echo FluentHDR installed. Open Display Mode from the Start menu or system tray.
pause
