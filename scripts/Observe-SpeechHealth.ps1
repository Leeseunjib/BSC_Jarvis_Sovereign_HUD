param([int]$Seconds=180,[switch]$IncludeTranscript)
$ErrorActionPreference='Stop'
[Console]::OutputEncoding=[Text.Encoding]::UTF8
$file=Join-Path (Split-Path -Parent $PSScriptRoot) ('data\verification\speech-live-'+(Get-Date -Format 'yyyyMMdd-HHmmss')+'.jsonl')
$clock=[Diagnostics.Stopwatch]::StartNew();$last='';$count=0
Write-Output $file
while($clock.Elapsed.TotalSeconds -lt $Seconds){
 try {
  $state=Invoke-RestMethod 'http://127.0.0.1:7788/api/ear/latest' -TimeoutSec 2
  $row=[ordered]@{ObservedAt=[DateTimeOffset]::Now.ToString('o');ElapsedSeconds=[math]::Round($clock.Elapsed.TotalSeconds,2);Health=$state.earHealth;Speaking=$state.isSpeaking;MicLevel=$state.micLevel}
  if($IncludeTranscript){$row.Latest=$state.latest}
  $signature=@($state.earHealth.recognition,$state.earHealth.error,$state.earHealth.event,$state.earHealth.turn.finalSegments,$state.earHealth.turn.interimChars,$state.isSpeaking)-join '|'
  if($IncludeTranscript){$signature+='|'+$state.latest.ts}
  if($signature -ne $last){$row|ConvertTo-Json -Depth 9 -Compress|Add-Content -LiteralPath $file -Encoding UTF8;$last=$signature;$count++}
 }catch{[pscustomobject]@{ObservedAt=[DateTimeOffset]::Now.ToString('o');Error=$_.Exception.Message}|ConvertTo-Json -Compress|Add-Content -LiteralPath $file -Encoding UTF8}
 Start-Sleep -Milliseconds 150
}
[pscustomobject]@{File=$file;Changes=$count;ObservedSeconds=[math]::Round($clock.Elapsed.TotalSeconds,2)}|ConvertTo-Json -Compress
