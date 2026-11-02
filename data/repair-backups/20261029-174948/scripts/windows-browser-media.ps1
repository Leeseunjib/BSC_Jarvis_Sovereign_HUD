$ErrorActionPreference='Stop'
[Console]::InputEncoding=[Text.Encoding]::UTF8
[Console]::OutputEncoding=[Text.Encoding]::UTF8
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
function VideoButtons($root) {
  $condition=New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty,[System.Windows.Automation.ControlType]::Button)
  $buttons=@($root.FindAll([System.Windows.Automation.TreeScope]::Descendants,$condition))
  return @{
    pause=@($buttons | Where-Object { $_.Current.Name -match '^(일시\s*중지|Pause)(\s|\(|$)' -and $_.Current.IsEnabled })
    play=@($buttons | Where-Object { $_.Current.Name -match '^(재생|Play)(\s|\(|$)' -and $_.Current.IsEnabled })
    next=@($buttons | Where-Object { $_.Current.Name -match '^(다음|Next)\s*\(' -and $_.Current.IsEnabled })
    prev=@($buttons | Where-Object { $_.Current.Name -match '^(이전|Previous)\s*\(' -and $_.Current.IsEnabled })
  }
}
function IsYouTubePage($root) {
  $all=$root.FindAll([System.Windows.Automation.TreeScope]::Descendants,[System.Windows.Automation.Condition]::TrueCondition)
  foreach($element in $all) {
    if($element.Current.ControlType -notin @([System.Windows.Automation.ControlType]::Edit,[System.Windows.Automation.ControlType]::Document)) { continue }
    $value=$null
    if($element.TryGetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern,[ref]$value)) {
      if([string]$value.Current.Value -match '^(https?://)?(www\.|m\.|music\.)?youtube\.com(?:/|$)') { return $true }
    }
  }
  return $false
}
function SelectTab($tab) {
  if($null -eq $tab) { return }
  $selection=$null
  if(-not $tab.TryGetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern,[ref]$selection)) { throw '유튜브 탭 선택 기능을 확인하지 못했습니다.' }
  $selection.Select()
  Start-Sleep -Milliseconds 350
}
$restore=@()
try {
  $request=[Console]::In.ReadToEnd()|ConvertFrom-Json
  $desktop=[System.Windows.Automation.AutomationElement]::RootElement
  $roots=@($desktop.FindAll([System.Windows.Automation.TreeScope]::Children,[System.Windows.Automation.Condition]::TrueCondition))
  $targets=@();$browserCount=0;$tabCount=0;$totalTabs=0;$diagnostics=@()
  foreach($root in $roots) {
    $proc=Get-Process -Id $root.Current.ProcessId -ErrorAction SilentlyContinue
    if($null -eq $proc -or $proc.ProcessName -notmatch '^(chrome|msedge|firefox|brave|opera)$' -or $root.Current.IsOffscreen) { continue }
    if($request.targetProcessId -and $root.Current.ProcessId -ne [int]$request.targetProcessId) { continue }
    $browserCount++
    $condition=New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty,[System.Windows.Automation.ControlType]::TabItem)
    $tabs=@($root.FindAll([System.Windows.Automation.TreeScope]::Descendants,$condition))
    $totalTabs += $tabs.Count
    $diagnostics += @{source=$proc.ProcessName;tabItems=$tabs.Count;youtubeWindow=([bool]($root.Current.Name -match '(?i)YouTube|유튜브'))}
    $original=$null
    foreach($tab in $tabs) { $pattern=$null; if($tab.TryGetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern,[ref]$pattern) -and $pattern.Current.IsSelected){$original=$tab;break} }
    $videos=@($tabs | Where-Object { $_.Current.Name -match '(?i)YouTube|유튜브' })
    $tabCount+=$videos.Count
    foreach($tab in $videos) { $targets+=@{root=$root;tab=$tab;original=$original;source=$proc.ProcessName} }
    if($videos.Count -eq 0 -and $root.Current.Name -match '(?i)YouTube|유튜브') { $targets+=@{root=$root;tab=$null;original=$original;source=$proc.ProcessName} }
  }
  if($request.action -eq 'status') {
    @{ok=$true;browserWindows=$browserCount;totalTabs=$totalTabs;youtubeTabs=$tabCount;youtubeCandidates=$targets.Count;diagnostics=$diagnostics}|ConvertTo-Json -Depth 4 -Compress;return
  }
  if($request.action -notin @('pause','play','next','prev')){throw '지원하지 않는 브라우저 미디어 명령입니다.'}
  if($targets.Count -gt 8){throw '유튜브 탭이 많아 대상을 자동으로 결정하지 못했습니다.'}
  $states=@()
  foreach($target in $targets) {
    try {
      if($target.original){$restore+=@($target.original)}
      SelectTab $target.tab
      if(-not (IsYouTubePage $target.root)){continue}
      $buttons=VideoButtons $target.root
      if($buttons.pause.Count -eq 1){$states+=@{target=$target;state='Playing'}}
      elseif($buttons.play.Count -eq 1){$states+=@{target=$target;state='Paused'}}
    } finally { if($target.original){try{SelectTab $target.original}catch{}} }
  }
  $wanted=if($request.action -eq 'play'){'Paused'}else{'Playing'}
  $active=@($states|Where-Object{$_.state -eq $wanted})
  if($active.Count -eq 0 -and $states.Count -ge 1 -and $request.action -notin @('next','prev')){
    @{ok=$true;state=$states[0].state;verified=$true;changed=$false;source=$states[0].target.source;method='youtube-accessibility'}|ConvertTo-Json -Compress;return
  }
  if($active.Count -eq 0){@{ok=$true;state='none';verified=$true;changed=$false;method='youtube-accessibility'}|ConvertTo-Json -Compress;return}
  $chosen=$active[0].target
  SelectTab $chosen.tab
  if(-not (IsYouTubePage $chosen.root)){throw '유튜브 주소를 확인하지 못해 조작하지 않았습니다.'}
  $buttons=VideoButtons $chosen.root
  $key=if($request.action -eq 'pause'){'pause'}elseif($request.action -eq 'play'){'play'}elseif($request.action -eq 'prev'){'prev'}else{'next'}
  $controlMatches=@($buttons[$key]);$invoke=$null
  if($controlMatches.Count -ne 1 -or -not $controlMatches[0].TryGetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern,[ref]$invoke)){throw '유튜브 재생 제어 버튼을 확인하지 못했습니다.'}
  $invoke.Invoke()
  if($request.action -in @('next','prev')){@{ok=$true;state='requested';verified=$false;changed=$true;source=$chosen.source;method='youtube-accessibility'}|ConvertTo-Json -Compress;return}
  $verified=$false
  for($i=0;$i -lt 10;$i++){
    Start-Sleep -Milliseconds 150
    $after=VideoButtons $chosen.root
    if(($request.action -eq 'pause' -and $after.play.Count -eq 1 -and $after.pause.Count -eq 0) -or ($request.action -eq 'play' -and $after.pause.Count -eq 1 -and $after.play.Count -eq 0)){$verified=$true;break}
  }
  if(-not $verified){throw '유튜브 버튼을 눌렀지만 재생 상태가 바뀐 것을 확인하지 못했습니다.'}
  @{ok=$true;state=$(if($request.action -eq 'pause'){'Paused'}else{'Playing'});verified=$true;changed=$true;source=$chosen.source;method='youtube-accessibility'}|ConvertTo-Json -Compress
} catch {
  @{ok=$false;error=$_.Exception.Message}|ConvertTo-Json -Compress;exit 1
} finally {
  foreach($tab in $restore){try{SelectTab $tab}catch{}}
}
