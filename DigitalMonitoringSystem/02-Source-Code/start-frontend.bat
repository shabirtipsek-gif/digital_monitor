@echo off
cd /d "%~dp0mdm-frontend"
if not exist node_modules npm install
npm run dev
pause
