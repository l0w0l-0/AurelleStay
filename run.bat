@echo off
rem ==== AurelleStay: double-click to start (Docker Desktop required) ====
chcp 65001 >nul
cd /d "%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0docker.ps1" up
if errorlevel 1 (
  echo.
  echo Start failed - see the messages above. Make sure Docker Desktop is running.
)
echo.
pause
