$srvPid = (Get-NetTCPConnection -LocalPort 7788 -State Listen -ErrorAction SilentlyContinue | Select-Object -First 1).OwningProcess
if ($srvPid) {
  Stop-Process -Id $srvPid -Force -ErrorAction SilentlyContinue
}
Start-Sleep -Milliseconds 600
Start-Process -FilePath "C:\Program Files\nodejs\node.exe" -ArgumentList "C:\자비스\server.verified.js" -WorkingDirectory "C:\자비스" -WindowStyle Hidden
