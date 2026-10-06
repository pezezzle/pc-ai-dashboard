@echo off
if exist "%~dp0artifacts\app\PcAiDashboard.exe" (
  start "" "%~dp0artifacts\app\PcAiDashboard.exe"
) else (
  echo Please run Build.ps1 -Publish first.
  pause
)
