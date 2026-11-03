@echo off
cd /d "%~dp0"
if not exist "Build\Release\DVMS.AIDialogueStudio.exe" (
  echo Build the Studio using Build Studio.ps1 or the Visual Studio solution first.
  pause
  exit /b 1
)
start "DVMS AI Dialogue Studio" "Build\Release\DVMS.AIDialogueStudio.exe"
