@echo off
setlocal
cd /d "%~dp0"
if not defined AGENT_CONTROL_REPO set "AGENT_CONTROL_REPO=%USERPROFILE%\local-ai-workspaces\mhw-mods"
start "Heaven Agent Control" /min node server.mjs
timeout /t 2 /nobreak >nul
start "" http://127.0.0.1:7331
