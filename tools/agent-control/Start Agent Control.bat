@echo off
setlocal
cd /d "%~dp0"
if /I not "%COMPUTERNAME%"=="heaven2" if not "%AGENT_CONTROL_ALLOW_NON_CONTROLLER_HOST%"=="1" (
  echo Agent Control is an operator surface and must run on heaven2. Current host: %COMPUTERNAME%
  echo Set AGENT_CONTROL_ALLOW_NON_CONTROLLER_HOST=1 only for an explicit recovery override.
  exit /b 2
)
if not defined AGENT_CONTROL_REPO set "AGENT_CONTROL_REPO=%USERPROFILE%\local-ai-workspaces\mhw-mods"

if exist "%~dp0Install-AgentControlShortcut.ps1" (
  powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Install-AgentControlShortcut.ps1" -RepoRoot "%AGENT_CONTROL_REPO%" >nul
  if errorlevel 1 (
    echo WARNING: Could not create or verify the Heaven Agent Control desktop shortcut.
  )
)

if exist "%~dp0Install-StartupRestore.ps1" (
  powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Install-StartupRestore.ps1" -RepoRoot "%AGENT_CONTROL_REPO%" >nul
  if errorlevel 1 (
    echo WARNING: Could not install or verify automatic startup restore.
  )
)

if not "%AGENT_CONTROL_SKIP_LOCAL_BRIDGE_BOOTSTRAP%"=="1" (
  if exist "%~dp0..\..\heaven-bridge\bootstrap.ps1" (
    echo Ensuring Heaven Local Bridge is installed on heaven2...
    powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0..\..\heaven-bridge\bootstrap.ps1"
    if errorlevel 1 (
      echo WARNING: Heaven2 bridge bootstrap failed. Agent Control will still open, but ChatGPT desktop control may report the local bridge as unavailable.
    )
  )
)

if not exist "%~dp0Start-AgentControlVerified.ps1" (
  echo ERROR: Verified Agent Control launcher is missing.
  exit /b 3
)

powershell -NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File "%~dp0Start-AgentControlVerified.ps1" -RepoRoot "%AGENT_CONTROL_REPO%" >nul
if errorlevel 1 (
  echo ERROR: Agent Control launch was blocked because canonical runtime freshness or listener ownership could not be proven.
  exit /b 4
)

start "" http://127.0.0.1:7331
