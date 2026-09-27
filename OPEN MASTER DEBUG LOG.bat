@echo off
setlocal
cd /d "%~dp0"
set "LOG=%~dp0MHW-DEBUG-ALL.log"
if not exist "%LOG%" (
  echo The master debug log does not exist yet:
  echo %LOG%
  echo.
  echo Run Build.bat, Test Everything.bat, or launch MHW Mod Manager once.
  pause
  exit /b 1
)
start "" notepad.exe "%LOG%"
exit /b 0
