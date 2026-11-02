param([ValidateRange(1,1440)][int]$Minutes=30,[ValidateRange(1,60)][int]$IntervalSeconds=5)
$ErrorActionPreference='Stop'
[Console]::OutputEncoding=[Text.Encoding]::UTF8
$project=Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'Check-HudWindows.ps1') | Out-Null
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
using System.Text;
public static class HudLivenessProbe {
 public static long LastResponseMs;
 delegate bool Callback(IntPtr w,IntPtr data);
 [DllImport("user32.dll")] static extern bool EnumWindows(Callback cb,IntPtr data);
 [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr w,out uint pid);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetWindowText(IntPtr w,StringBuilder text,int capacity);
 [DllImport("user32.dll")] static extern IntPtr SendMessageTimeout(IntPtr w,uint msg,IntPtr wp,IntPtr lp,uint flags,uint timeout,out IntPtr result);
 [DllImport("user32.dll")] public static extern uint GetGuiResources(IntPtr process,uint flags);
 public static bool Responsive(uint pid) {
  IntPtr found=IntPtr.Zero;
  EnumWindows((w,data)=>{uint owner;GetWindowThreadProcessId(w,out owner);if(owner==pid){var text=new StringBuilder(256);GetWindowText(w,text,256);if(text.ToString().Contains("Monitor Top HUD")){found=w;return false;}}return true;},IntPtr.Zero);
  if(found==IntPtr.Zero)return false;IntPtr result;
  var clock=System.Diagnostics.Stopwatch.StartNew();
  bool responsive=SendMessageTimeout(found,0,IntPtr.Zero,IntPtr.Zero,2,750,out result)!=IntPtr.Zero;
  LastResponseMs=clock.ElapsedMilliseconds;
  return responsive;
 }
}
'@
$installed=Join-Path $project 'bin\JarvisHud.exe'
$initial=@(Get-Process -Name JarvisHud -ErrorAction SilentlyContinue | Where-Object {$_.Path -ieq $installed})
if($initial.Count -ne 1){throw 'Expected exactly one installed HUD.'}
$initialId=$initial[0].Id
$initial[0].Dispose()
$folder=Join-Path $project 'data\verification'
New-Item -ItemType Directory -Path $folder -Force | Out-Null
$runId=Get-Date -Format 'yyyyMMdd-HHmmss'
$jsonl=Join-Path $folder ('hud-soak-'+$runId+'.jsonl')
$summaryPath=Join-Path $folder ('hud-soak-'+$runId+'-summary.json')
$clock=[Diagnostics.Stopwatch]::StartNew()
$started=[DateTime]::Now.ToString('o')
$samples=New-Object 'Collections.Generic.List[object]'
$lastMinute=-1
$stopFile=Join-Path $folder ('hud-soak-'+$runId+'.stop')
Write-Output ('Started stability observation: '+$jsonl+'; observerPid='+$PID+'; stopFile='+$stopFile)
do {
 $items=@(Get-Process -Name JarvisHud -ErrorAction SilentlyContinue | Where-Object {$_.Path -ieq $installed})
 $global=@([JarvisWindowInventory]::OnscreenHudWindows())
 $placements=@($items | ForEach-Object {[JarvisWindowInventory]::Placements([uint32]$_.Id)})
 $onePrimary=$placements.Count -eq 1 -and $placements[0].Primary -and $placements[0].Topmost
 $p=if($items.Count -eq 1){$items[0]}else{$null}
 $responsive=$false;$privateMb=$null;$handles=$null;$gdi=$null;$user=$null;$cpu=$null;$threads=$null
 if($null -ne $p){
  try {$p.Refresh();$responsive=[HudLivenessProbe]::Responsive([uint32]$p.Id);$privateMb=[math]::Round($p.PrivateMemorySize64/1MB,2);$handles=$p.HandleCount;$gdi=[HudLivenessProbe]::GetGuiResources($p.Handle,0);$user=[HudLivenessProbe]::GetGuiResources($p.Handle,1);$cpu=[math]::Round($p.TotalProcessorTime.TotalSeconds,2);$threads=$p.Threads.Count}catch{}
 }
 $serverOk=$false;$serverPid=$null;$micConnected=$false
 try {$health=Invoke-RestMethod 'http://127.0.0.1:7788/api/health' -TimeoutSec 2;$serverOk=$health.ok -and $health.service -eq 'jarvis';$serverPid=$health.pid}catch{}
 try {$ear=Invoke-RestMethod 'http://127.0.0.1:7788/api/ear/health' -TimeoutSec 2;$micConnected=[bool]$ear.connected}catch{}
 $samePid=$null -ne $p -and $p.Id -eq $initialId
 $passed=$items.Count -eq 1 -and $global.Count -eq 1 -and $onePrimary -and $responsive -and $samePid -and $serverOk
 $row=[pscustomobject]@{Time=[DateTime]::Now.ToString('o');ElapsedSeconds=[math]::Round($clock.Elapsed.TotalSeconds,2);Pid=if($p){$p.Id}else{$null};SamePid=$samePid;ProcessCount=$items.Count;VisibleHudCount=$global.Count;PrimaryTopmost=$onePrimary;Placements=$placements;Responsive=$responsive;ResponseMs=[HudLivenessProbe]::LastResponseMs;PrivateMb=$privateMb;Handles=$handles;GdiObjects=$gdi;UserObjects=$user;Threads=$threads;CpuSeconds=$cpu;ServerOk=$serverOk;ServerPid=$serverPid;MicConnected=$micConnected;Passed=$passed}
 $samples.Add($row);$row|ConvertTo-Json -Depth 4 -Compress|Add-Content -LiteralPath $jsonl -Encoding UTF8
 foreach($item in $items){$item.Dispose()}
 $minute=[int][math]::Floor($clock.Elapsed.TotalMinutes)
 if($minute -ne $lastMinute -or -not $passed){$row|ConvertTo-Json -Depth 4 -Compress;$lastMinute=$minute}
 if(Test-Path -LiteralPath $stopFile){break}
 if($clock.Elapsed.TotalMinutes -lt $Minutes){Start-Sleep -Seconds $IntervalSeconds}
} while($clock.Elapsed.TotalMinutes -lt $Minutes)
$last=$samples[$samples.Count-1]
$summary=[pscustomobject]@{StartedAt=$started;EndedAt=[DateTime]::Now.ToString('o');ObservedSeconds=[math]::Round($clock.Elapsed.TotalSeconds,2);RequestedMinutes=$Minutes;CompletedDuration=$clock.Elapsed.TotalMinutes -ge $Minutes;Samples=$samples.Count;InitialPid=$initialId;FinalPid=$last.Pid;Passed=($clock.Elapsed.TotalMinutes -ge $Minutes) -and @($samples|Where-Object {-not $_.Passed}).Count -eq 0;FailureSamples=@($samples|Where-Object {-not $_.Passed});MicDisconnectedSamples=@($samples|Where-Object {-not $_.MicConnected}).Count;MinPrivateMb=($samples.PrivateMb|Measure-Object -Minimum).Minimum;MaxPrivateMb=($samples.PrivateMb|Measure-Object -Maximum).Maximum;StartPrivateMb=$samples[0].PrivateMb;EndPrivateMb=$last.PrivateMb;MinHandles=($samples.Handles|Measure-Object -Minimum).Minimum;MaxHandles=($samples.Handles|Measure-Object -Maximum).Maximum;MaxGdiObjects=($samples.GdiObjects|Measure-Object -Maximum).Maximum;MaxUserObjects=($samples.UserObjects|Measure-Object -Maximum).Maximum;DataFile=$jsonl}
$summary|ConvertTo-Json -Depth 5|Set-Content -LiteralPath $summaryPath -Encoding UTF8
$summary|ConvertTo-Json -Depth 5 -Compress
if(-not $summary.Passed){exit 1}
