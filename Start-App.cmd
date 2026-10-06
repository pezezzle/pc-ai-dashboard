@echo off
if exist "%~dp0artifacts\app\PcAiDashboard.exe" (
  start "" "%~dp0artifacts\app\PcAiDashboard.exe"
) else (
  echo Bitte zuerst Build.ps1 -Publish ausfuehren.
  pause
)
