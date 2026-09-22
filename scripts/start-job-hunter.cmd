@echo off
setlocal

REM Windows Explorer launcher. ExecutionPolicy applies only to this child process.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0start-job-hunter.ps1"
set "worker_exit_code=%ERRORLEVEL%"

if not "%worker_exit_code%"=="0" (
    echo.
    echo Job Hunter stopped with exit code %worker_exit_code%.
    pause
)

exit /b %worker_exit_code%
