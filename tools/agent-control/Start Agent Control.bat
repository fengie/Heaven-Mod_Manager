@echo off
setlocal
cd /d "%~dp0"
if /I not "%COMPUTERNAME%"=="heaven2" if not "%AGENT_CONTROL_ALLOW_NON_CONTROLLER_HOST%"=="1" (
  echo Agent Control is an operator surface and must run on heaven2. Current host: %COMPUTERNAME%
  echo Set AGENT_CONTROL_ALLOW_NON_CONTROLLER_HOST=1 only for an explicit recovery override.
  exit /b 2
)
if not defined AGENT_CONTROL_REPO set "AGENT_CONTROL_REPO=%USERPROFILE%\local-ai-workspaces\mhw-mods"
start "Heaven2 Agent Control" /min node server.mjs
timeout /t 2 /nobreak >nul
start "" http://127.0.0.1:7331
