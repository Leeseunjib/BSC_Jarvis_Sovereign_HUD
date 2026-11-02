$ErrorActionPreference = 'Stop'
[Console]::InputEncoding = [Text.Encoding]::UTF8
[Console]::OutputEncoding = [Text.Encoding]::UTF8
try {
  $request = [Console]::In.ReadToEnd() | ConvertFrom-Json
  Add-Type -AssemblyName System.Runtime.WindowsRuntime
  $managerType = [Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager, Windows.Media.Control, ContentType=WindowsRuntime]
  $asTask = [System.WindowsRuntimeSystemExtensions].GetMethods() | Where-Object { $_.Name -eq 'AsTask' -and $_.IsGenericMethod -and $_.GetGenericArguments().Count -eq 1 -and $_.GetParameters().Count -eq 1 -and $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncOperation`1' } | Select-Object -First 1
  function Await($operation, [Type]$resultType) {
    $task = $asTask.MakeGenericMethod($resultType).Invoke($null, @($operation))
    if (-not $task.Wait(3500)) { throw 'Windows 미디어 제어 응답 시간이 초과되었습니다.' }
    return $task.Result
  }
  $manager = Await ($managerType::RequestAsync()) $managerType
  $current = $manager.GetCurrentSession()
  $sessions = @($manager.GetSessions())
  $rows = @()
  foreach ($session in $sessions) {
    $source = [string]$session.SourceAppUserModelId
    $info = $session.GetPlaybackInfo()
    $title = ''
    $artist = ''
    try {
      $props = Await ($session.TryGetMediaPropertiesAsync()) ([Windows.Media.Control.GlobalSystemMediaTransportControlsSessionMediaProperties])
      if ($null -ne $props) {
        $title = [string]$props.Title
        $artist = [string]$props.Artist
      }
    } catch {}
    $rows += @{ session=$session; source=$source; browser=($source -match '(?i)chrome|msedge|firefox|brave|opera|whale|youtube'); status=[string]$info.PlaybackStatus; title=$title; artist=$artist; current=($null -ne $current -and [Object]::ReferenceEquals($current,$session)) }
  }
  if ($request.action -eq 'status') {
    @{ ok=$true; sessions=@($rows | ForEach-Object { @{ source=$_.source; browser=$_.browser; status=$_.status; title=$_.title; artist=$_.artist; current=$_.current } }) } | ConvertTo-Json -Depth 4 -Compress
    return
  }
  if ($request.action -notin @('pause','play','next','prev')) { throw '지원하지 않는 미디어 명령입니다.' }
  $candidates = @($rows | Where-Object { $_.status -ne 'Closed' -and ($request.scope -ne 'browser' -or $_.browser) })
  if ($request.preferredSource) {
    $preferred = @($candidates | Where-Object { $_.source -eq $request.preferredSource })
    if ($preferred.Count) { $candidates = $preferred }
  }
  $playing = @($candidates | Where-Object { $_.status -eq 'Playing' })
  $currentRows = @($candidates | Where-Object { $_.current })
  $selected = $null
  if ($request.action -eq 'pause' -and $playing.Count -ge 1) { $selected=$playing[0] }
  elseif ($currentRows.Count -ge 1) { $selected=$currentRows[0] }
  elseif ($candidates.Count -ge 1) { $selected=$candidates[0] }
  if ($null -eq $selected) {
    if ($candidates.Count -eq 0) { @{ ok=$true; state='none'; verified=$true; changed=$false } | ConvertTo-Json -Compress; return }
    $selected = $candidates[0]
  }
  $session=$selected.session
  $before=[string]$session.GetPlaybackInfo().PlaybackStatus
  if ($request.action -eq 'pause' -and $before -in @('Paused','Stopped','Closed')) {
    @{ok=$true;state=$before;verified=$true;changed=$false;source=$selected.source}|ConvertTo-Json -Compress; return
  }
  if ($request.action -eq 'play' -and $before -eq 'Playing') {
    @{ok=$true;state=$before;verified=$true;changed=$false;source=$selected.source}|ConvertTo-Json -Compress; return
  }
  $attempts=@()
  if ($request.action -eq 'pause') {
    # Each alternative is attempted only while the same session is still playing.
    foreach ($method in @('pause','stop','toggle')) {
      if ([string]$session.GetPlaybackInfo().PlaybackStatus -ne 'Playing') { break }
      $controls=$session.GetPlaybackInfo().Controls
      $accepted=$false
      if ($method -eq 'pause' -and $controls.IsPauseEnabled) { $accepted=Await ($session.TryPauseAsync()) ([bool]) }
      elseif ($method -eq 'stop' -and $controls.IsStopEnabled) { $accepted=Await ($session.TryStopAsync()) ([bool]) }
      elseif ($method -eq 'toggle' -and $controls.IsPlayPauseToggleEnabled) { $accepted=Await ($session.TryTogglePlayPauseAsync()) ([bool]) }
      else { continue }
      $attempts += $method
      for ($i=0;$i -lt 8;$i++) {
        Start-Sleep -Milliseconds 150
        if ([string]$session.GetPlaybackInfo().PlaybackStatus -in @('Paused','Stopped','Closed')) { break }
      }
    }
  } elseif ($request.action -eq 'play') {
    if (-not $session.GetPlaybackInfo().Controls.IsPlayEnabled) { throw '이 앱은 현재 다시 재생을 지원하지 않습니다.' }
    [void](Await ($session.TryPlayAsync()) ([bool])); $attempts += 'play'
  } elseif ($request.action -eq 'prev') {
    if (-not $session.GetPlaybackInfo().Controls.IsPreviousEnabled) { throw '현재 영상은 이전 곡 제어를 지원하지 않습니다.' }
    $accepted=Await ($session.TrySkipPreviousAsync()) ([bool])
    @{ok=[bool]$accepted;state='requested';verified=$false;changed=[bool]$accepted;source=$selected.source}|ConvertTo-Json -Compress; return
  } else {
    if (-not $session.GetPlaybackInfo().Controls.IsNextEnabled) { throw '현재 영상은 다음 곡 제어를 지원하지 않습니다.' }
    $accepted=Await ($session.TrySkipNextAsync()) ([bool])
    @{ok=[bool]$accepted;state='requested';verified=$false;changed=[bool]$accepted;source=$selected.source}|ConvertTo-Json -Compress; return
  }
  $after=[string]$session.GetPlaybackInfo().PlaybackStatus
  for ($i=0;$request.action -eq 'play' -and $after -ne 'Playing' -and $i -lt 8;$i++) { Start-Sleep -Milliseconds 150; $after=[string]$session.GetPlaybackInfo().PlaybackStatus }
  $verified=($request.action -eq 'pause' -and $after -in @('Paused','Stopped','Closed')) -or ($request.action -eq 'play' -and $after -eq 'Playing')
  if (-not $verified) { throw '재생 앱이 요청한 상태로 바뀌지 않았습니다. 다른 창을 닫거나 재생을 임의로 다시 시작하지 않았습니다.' }
  @{ok=$true;state=$after;verified=$true;changed=($before -ne $after);source=$selected.source;attempts=$attempts}|ConvertTo-Json -Depth 4 -Compress
} catch {
  @{ok=$false;error=$_.Exception.Message}|ConvertTo-Json -Compress
  exit 1
}
