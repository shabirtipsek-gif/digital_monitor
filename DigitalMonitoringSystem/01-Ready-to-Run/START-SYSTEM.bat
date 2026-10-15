@echo off
title Digital Monitoring System - Server
cd /d "%~dp0"
if not exist "node\node.exe" (
  echo node\node.exe is missing. Extract the whole zip file first, then run this again.
  pause
  exit /b 1
)
start "" /min cmd /c "timeout /t 3 >nul & start http://localhost:5103"
"node\node.exe" --disable-warning=ExperimentalWarning server\server.js
echo.
echo The server has stopped.
pause
