@echo off
setlocal EnableExtensions
chcp 65001 >nul
title PET Digital Twin - despliegue
cd /d "%~dp0"

set "PY=.venv\Scripts\python.exe"

if not exist "%PY%" (
    echo.
    echo PET Digital Twin - preparacion inicial
    echo Creando entorno virtual en .venv ...
    where py >nul 2>nul
    if errorlevel 1 (
        python -m venv .venv
    ) else (
        py -3.11 -m venv .venv || py -3 -m venv .venv || py -m venv .venv
    )
)

if not exist "%PY%" (
    echo.
    echo ERROR: no se pudo crear .venv. Instala Python 3.11 o superior y vuelve a ejecutar deploy.bat.
    echo Descarga: https://www.python.org/downloads/
    echo.
    pause
    exit /b 1
)

"%PY%" -c "import simpy, fastapi, uvicorn, pydantic" >nul 2>nul
if errorlevel 1 (
    echo.
    echo Instalando dependencias del proyecto ...
    "%PY%" -m pip install --upgrade pip
    if errorlevel 1 goto install_error
    "%PY%" -m pip install -e .
    if errorlevel 1 goto install_error
)

"%PY%" -m deployment.launcher %*
set "RC=%ERRORLEVEL%"

echo.
pause
exit /b %RC%

:install_error
echo.
echo ERROR: no se pudieron instalar las dependencias.
echo Revisa tu conexion a internet y vuelve a ejecutar deploy.bat.
echo.
pause
exit /b 1
