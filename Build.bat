@echo off
setlocal
cd /d "%~dp0"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\testing\Test-PowerShellSyntax.ps1"
if errorlevel 1 (
  echo.
  echo Build FAILED because an active PowerShell script has a syntax error.
  pause
  exit /b 3
)
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\testing\Test-CSharpTracePlacement.ps1"
if errorlevel 1 (
  echo.
  echo C# TRACE PLACEMENT PREFLIGHT FAILED. Upload MHW-DEBUG-ALL.log from this folder.
  pause
  exit /b 4
)
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\build\Build-Release.ps1"
set ERR=%ERRORLEVEL%
echo.
if not "%ERR%"=="0" (
  echo Build FAILED. Upload MHW-DEBUG-ALL.log from this folder.
) else (
  echo Build completed successfully. Master log: MHW-DEBUG-ALL.log
)
pause
exit /b %ERR%
