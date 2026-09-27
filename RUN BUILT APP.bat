@echo off
setlocal
cd /d "%~dp0"

set "ROOT=%~dp0"
for /f "usebackq delims=" %%V in ("%ROOT%VERSION.txt") do set "VERSION=%%V"
set "APPDIR=%ROOT%release\MHW-Manual-Mod-Manager-v%VERSION%"
set "APP=%APPDIR%\MHW Mod Manager.exe"
set "MASTER=%ROOT%MHW-DEBUG-ALL.log"

if not exist "%APP%" (
  echo Built application not found:
  echo %APP%
  echo.
  echo Run Build.bat first.
  pause
  exit /b 2
)

set "MHW_MANAGER_HOME=%ROOT:~0,-1%"
set "MHW_MASTER_DEBUG_ROOT=%ROOT:~0,-1%"

>>"%MASTER%" echo.
>>"%MASTER%" echo ================================================================================
>>"%MASTER%" echo %DATE% %TIME% [ROOT-LAUNCHER] Launching built v%VERSION%
>>"%MASTER%" echo %DATE% %TIME% [ROOT-LAUNCHER] EXE=%APP%
>>"%MASTER%" echo %DATE% %TIME% [ROOT-LAUNCHER] MHW_MANAGER_HOME=%MHW_MANAGER_HOME%
>>"%MASTER%" echo %DATE% %TIME% [ROOT-LAUNCHER] MHW_MASTER_DEBUG_ROOT=%MHW_MASTER_DEBUG_ROOT%

echo Launching MHW Mod Manager v%VERSION%...
echo.
echo Runtime diagnostics will append to:
echo %MASTER%
echo.

"%APP%"
set "ERR=%ERRORLEVEL%"

>>"%MASTER%" echo %DATE% %TIME% [ROOT-LAUNCHER] Process exited with code %ERR%
echo.
echo App exited with code %ERR%.
echo If that was unexpected, upload MHW-DEBUG-ALL.log from this folder.
pause
exit /b %ERR%
