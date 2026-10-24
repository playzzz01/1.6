@echo off
chcp 65001 >nul
set "SCRIPT=%~dp0启动修改器.ps1"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%SCRIPT%"
