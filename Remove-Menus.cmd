@echo off
cd /d "%~dp0"
echo Close Context Menu Studio before continuing.
echo This removes only menus installed by Context Menu Studio. Your draft is kept.
choice /M "Continue"
if errorlevel 2 exit /b 0
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\remove-menus.ps1"
if errorlevel 1 (
  echo Removal failed. Read the error above.
  pause
  exit /b 1
)
pause
