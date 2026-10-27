@echo off
cd /d "%~dp0"
if exist "%~dp0artifacts\app\ContextMenuStudio.Desktop.exe" (
  start "" "%~dp0artifacts\app\ContextMenuStudio.Desktop.exe"
  exit /b 0
)
if exist "%~dp0app\ContextMenuStudio.Desktop.exe" (
  start "" "%~dp0app\ContextMenuStudio.Desktop.exe"
  exit /b 0
)
echo Run Build.cmd first to build the application. It requires the .NET 10 SDK.
pause
exit /b 1
