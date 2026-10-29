@echo off
chcp 65001 >nul
title 자비스 (SasariSa Pure Desktop HUD)
cd /d "%~dp0"
taskkill /F /IM JarvisHud.exe >nul 2>&1
if exist "%~dp0bin\JarvisHud.next.exe" (
  copy /Y "%~dp0bin\JarvisHud.next.exe" "%~dp0bin\JarvisHud.exe" >nul 2>&1
  copy /Y "%~dp0bin\JarvisHud.next.exe" "%~dp0JarvisHud.exe" >nul 2>&1
)
start "" powershell.exe -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -Command "Get-CimInstance Win32_Process | Where-Object { ($_.Name -eq 'node.exe' -and $_.CommandLine -match 'server\.js') -or ($_.Name -eq 'chrome.exe' -and $_.CommandLine -match 'chrome-ear-profile|jarvis-ear-worker') } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }; Start-Process -FilePath 'node.exe' -ArgumentList '%~dp0server.js' -WorkingDirectory '%~dp0' -WindowStyle Hidden; Start-Sleep -Milliseconds 900; & '%~dp0scripts\Open-JarvisHud.ps1'"
exit
