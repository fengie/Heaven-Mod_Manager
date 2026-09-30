@echo off
setlocal
cd /d "%~dp0"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\testing\Test-PowerShellSyntax.ps1" -VerifierOnly
if errorlevel 1 (
  echo.
  echo Verification harness FAILED syntax preflight.
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
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\release\Verify-Release.ps1"
set ERR=%ERRORLEVEL%
echo.
if not "%ERR%"=="0" (
  echo Verification FAILED. Upload MHW-DEBUG-ALL.log from this folder.
) else (
  echo Verification PASSED. Master log: MHW-DEBUG-ALL.log
)
pause
exit /b %ERR%
