@echo off
setlocal
chcp 65001 >nul
title PET Digital Twin - despliegue
cd /d "%~dp0"

set "PY=.venv\Scripts\python.exe"
if not exist "%PY%" set "PY=python"

"%PY%" -m deployment.launcher %*
set "RC=%ERRORLEVEL%"

echo.
pause
exit /b %RC%
