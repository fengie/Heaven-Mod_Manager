@echo off
setlocal
set "ROOT=%MHW_MANAGER_HOME%"
if "%ROOT%"=="" set "ROOT=%~dp0"
set "LOGDIR=%ROOT%\StartupLogs"
if not exist "%LOGDIR%" (
  echo No startup logs exist yet:
  echo %LOGDIR%
  echo.
  echo Launch MHW Mod Manager once, then run this again.
  pause
  exit /b 1
)
start "" "%LOGDIR%"
exit /b 0
