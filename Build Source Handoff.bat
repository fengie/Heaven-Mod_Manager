@echo off
setlocal
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\build\Build-Source-Handoff.ps1"
if errorlevel 1 pause
endlocal
