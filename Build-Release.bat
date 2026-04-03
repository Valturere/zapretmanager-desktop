@echo off
setlocal
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Build-Portable.ps1" -Mode Release
set "EXIT_CODE=%ERRORLEVEL%"
if /I not "%~1"=="--no-pause" pause
exit /b %EXIT_CODE%
