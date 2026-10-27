@echo off
cd /d "%~dp0"
set "CMS_APP_DIR=%~dp0app"
if exist "%~dp0artifacts\app\ContextMenuStudio.Desktop.exe" set "CMS_APP_DIR=%~dp0artifacts\app"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\smoke-test.ps1" -AppDirectory "%CMS_APP_DIR%"
if errorlevel 1 (
  echo Launch verification failed. Read the error above.
  pause
  exit /b 1
)
echo Both Windows applications rendered successfully.
pause
