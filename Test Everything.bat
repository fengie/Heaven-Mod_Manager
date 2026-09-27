@echo off
setlocal
cd /d "%~dp0"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\Test-PowerShellSyntax.ps1" -VerifierOnly
if errorlevel 1 (
  echo.
  echo VERIFIER SCRIPT FAILED POWERSHELL SYNTAX PREFLIGHT.
  echo The full application test cannot start until Verify-Release.ps1 parses correctly.
  pause
  exit /b 3
)
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\Test-CSharpTracePlacement.ps1"
if errorlevel 1 (
  echo.
  echo C# TRACE PLACEMENT PREFLIGHT FAILED. Upload MHW-DEBUG-ALL.log from this folder.
  pause
  exit /b 4
)
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\Verify-Release.ps1"
set ERR=%ERRORLEVEL%
echo.
if not "%ERR%"=="0" (
  echo TESTS FINISHED WITH FAILURES. Upload MHW-DEBUG-ALL.log from this folder.
) else (
  echo ALL TEST GROUPS PASSED. Master log: MHW-DEBUG-ALL.log
)
pause
exit /b %ERR%
