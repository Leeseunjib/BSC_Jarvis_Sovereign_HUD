$ErrorActionPreference = 'Continue'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$logFile = "C:\자비스\data\live-verify-report.txt"
"=== JARVIS LIVE DEPLOY & VERIFY START ($(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')) ===" | Set-Content -LiteralPath $logFile -Encoding UTF8

# 1. Stop old JarvisHud, server.js, and any old chrome-ear-profile
Get-Process -Name "JarvisHud*" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Get-CimInstance Win32_Process | Where-Object {
  ($_.Name -eq 'node.exe' -and $_.CommandLine -match '자비스\\server\.js') -or
  ($_.Name -eq 'chrome.exe' -and $_.CommandLine -match 'chrome-ear-profile|jarvis-ear-worker')
} | ForEach-Object {
  Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue
}
Start-Sleep -Milliseconds 800

# 2. Copy newly compiled JarvisHud.next.exe to JarvisHud.exe
Copy-Item -LiteralPath "C:\자비스\bin\JarvisHud.next.exe" -Destination "C:\자비스\bin\JarvisHud.exe" -Force
Copy-Item -LiteralPath "C:\자비스\bin\JarvisHud.next.exe" -Destination "C:\자비스\JarvisHud.exe" -Force
"HUD_BINARY_UPDATED: OK" | Add-Content -LiteralPath $logFile -Encoding UTF8

# 3. Start server.js and JarvisHud.exe on Winsta0\Default
Start-Process -FilePath "C:\Program Files\nodejs\node.exe" -ArgumentList "C:\자비스\server.js" -WorkingDirectory "C:\자비스" -WindowStyle Hidden
Start-Sleep -Milliseconds 1500
Start-Process -FilePath "C:\자비스\bin\JarvisHud.exe" -WorkingDirectory "C:\자비스"
Start-Sleep -Milliseconds 3500

# 4. Clear speaking flag and test Gemini 3.8 Flash Audio STT (/api/ear/transcribe-audio)
$null = Invoke-RestMethod -Uri "http://127.0.0.1:7788/api/ear/speaking" -Method Post -ContentType "application/json" -Body '{"speaking":false}' -TimeoutSec 5
$ttsBytes = Invoke-WebRequest -Uri "http://127.0.0.1:7788/api/tts-mp3?voice=ko-KR-HyunsuMultilingualNeural&text=$([Uri]::EscapeDataString('자비스 깃 상태 확인해줘'))" -UseBasicParsing -TimeoutSec 10
$b64 = [Convert]::ToBase64String($ttsBytes.Content)
$sttResp = Invoke-RestMethod -Uri "http://127.0.0.1:7788/api/ear/transcribe-audio" -Method Post -ContentType "application/json" -Body (@{ audioBase64 = $b64; mimeType = "audio/mpeg" } | ConvertTo-Json -Compress) -TimeoutSec 15
"STT_TEST: $($sttResp | ConvertTo-Json -Compress)" | Add-Content -LiteralPath $logFile -Encoding UTF8

# 5. Test CEO Intelligence & CHLGD + Harness2 Order Logging
$ceoResp = Invoke-RestMethod -Uri "http://127.0.0.1:7788/api/butler/command" -Method Post -ContentType "application/json; charset=utf-8" -Body ([System.Text.Encoding]::UTF8.GetBytes('{"sentence":"나에 대해 뭐 알고 있어","executeOs":true}')) -TimeoutSec 8
"CEO_INTEL: $($ceoResp.result.spoken)" | Add-Content -LiteralPath $logFile -Encoding UTF8

$logCmdResp = Invoke-RestMethod -Uri "http://127.0.0.1:7788/api/butler/command" -Method Post -ContentType "application/json; charset=utf-8" -Body ([System.Text.Encoding]::UTF8.GetBytes('{"sentence":"오더 로그 보여줘","executeOs":true}')) -TimeoutSec 8
"CHLGD_LOG_TITLE: $($logCmdResp.result.hudTitle)" | Add-Content -LiteralPath $logFile -Encoding UTF8

# 6. Test Music Play/Pause Toggle & Next Track (dry-run and live state)
$toggleDry = Invoke-RestMethod -Uri "http://127.0.0.1:7788/api/butler/command" -Method Post -ContentType "application/json; charset=utf-8" -Body ([System.Text.Encoding]::UTF8.GetBytes('{"sentence":"재생 일시정지 토글","executeOs":false}')) -TimeoutSec 8
"MUSIC_TOGGLE_TITLE: $($toggleDry.result.hudTitle) | INTENT: $($toggleDry.result.intent)" | Add-Content -LiteralPath $logFile -Encoding UTF8

# 7. Check Ear Health & Subwindows
Start-Sleep -Milliseconds 1500
$ear = Invoke-RestMethod -Uri "http://127.0.0.1:7788/api/ear/latest" -Method Get -TimeoutSec 5
"EAR_HEALTH: $($ear.earHealth | ConvertTo-Json -Compress) | MIC_LEVEL: $($ear.micLevel)" | Add-Content -LiteralPath $logFile -Encoding UTF8

$sub = Invoke-RestMethod -Uri "http://127.0.0.1:7788/api/hud/subwindows" -Method Get -TimeoutSec 5
"SUBWINDOWS: $($sub | ConvertTo-Json -Depth 4 -Compress)" | Add-Content -LiteralPath $logFile -Encoding UTF8

# 8. Git commit & push
Set-Location -LiteralPath "C:\자비스"
$gitOut = git add -A 2>&1
$gitCommit = git commit -m "feat(ear,music,chlgd): add Gemini 3.8 Flash hybrid VAD audio STT, fix music play/pause/next controller, and integrate CHLGD + Harness2 CEO order logging & self-growth" 2>&1
$gitPush = git push origin main 2>&1
"GIT_COMMIT: $gitCommit" | Add-Content -LiteralPath $logFile -Encoding UTF8
"GIT_PUSH: $gitPush" | Add-Content -LiteralPath $logFile -Encoding UTF8
"=== DONE ===" | Add-Content -LiteralPath $logFile -Encoding UTF8
