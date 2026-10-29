@echo off
chcp 65001 >nul
title 자비스 (SasariSa Pure Desktop HUD)
cd /d "%~dp0"
start "" powershell.exe -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File "%~dp0scripts\Open-JarvisHud.ps1"
exit
