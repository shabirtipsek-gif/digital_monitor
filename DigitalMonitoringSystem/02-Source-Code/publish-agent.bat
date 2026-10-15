@echo off
cd /d "%~dp0DigitalMonitoringSystem.Agent"
dotnet publish -c Release -r win-x64 --self-contained true -o publish
pause
