@echo off
rem Run as Administrator. Makes the agent start by itself every time this computer starts (runs hidden).
schtasks /create /tn "Digital Monitoring System Agent" /tr "\"%~dp0RUN-AGENT.bat\"" /sc onstart /ru SYSTEM /rl highest /f
if errorlevel 1 (echo Failed. Right-click this file and choose Run as administrator.) else (echo Installed. The agent will start automatically at boot. To start it now run RUN-AGENT.bat.)
pause
