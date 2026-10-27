@echo off
cd /d "%~dp0"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\build.ps1" %*
if errorlevel 1 (
  echo.
  echo Build failed. Read the errors above.
  pause
  exit /b 1
)
echo.
echo Ready. Double-click Launch.cmd to start.
pause
