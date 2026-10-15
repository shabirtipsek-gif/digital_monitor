@echo off
title Digital Monitoring System - Agent
cd /d "%~dp0"
set NODE=node
if exist "%~dp0node\node.exe" set NODE="%~dp0node\node.exe"
if exist "%~dp0..\node\node.exe" set NODE="%~dp0..\node\node.exe"
:loop
%NODE% --disable-warning=ExperimentalWarning agent.js
if errorlevel 2 (pause & exit /b 2)
echo Agent stopped. Restarting in 10 seconds...
timeout /t 10 >nul
goto loop
