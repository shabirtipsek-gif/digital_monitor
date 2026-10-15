@echo off
title Digital Monitoring System
set "SYSTEM_DIR=%~dp0"
set "SYSTEM_DIR=%SYSTEM_DIR%01-Ready-to-Run"
if not exist "%SYSTEM_DIR%\START-SYSTEM.bat" (
  echo The ready-to-run system is missing from 01-Ready-to-Run.
  pause
  exit /b 1
)
call "%SYSTEM_DIR%\START-SYSTEM.bat"
