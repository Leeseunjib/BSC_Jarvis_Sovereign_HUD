$ErrorActionPreference='Stop'
[Console]::OutputEncoding=[Text.Encoding]::UTF8
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class RecoveryResponseProbe {
 [DllImport("user32.dll")] static extern IntPtr SendMessageTimeout(IntPtr window,uint message,IntPtr wp,IntPtr lp,uint flags,uint timeout,out IntPtr result);
 public static bool Responds(long window) { IntPtr result;return SendMessageTimeout(new IntPtr(window),0,IntPtr.Zero,IntPtr.Zero,2,750,out result)!=IntPtr.Zero; }
}
'@
$hud=Get-Process -Id 12640
if($hud.Path -ine 'C:\자비스\bin\JarvisHud.exe'){throw 'HUD identity mismatch.'}
$worker=Get-CimInstance Win32_Process -Filter "ProcessId=25508"
if($worker.Name -ne 'chrome.exe' -or $worker.CommandLine -notlike '*C:\자비스\data\chrome-ear-profile*' -or $worker.CommandLine -like '*--type=*'){throw 'Dedicated worker identity mismatch.'}
$before=Invoke-RestMethod 'http://127.0.0.1:7788/api/ear/health' -TimeoutSec 2
Stop-Process -Id $worker.ProcessId -Force
$clock=[Diagnostics.Stopwatch]::StartNew()
$rows=New-Object 'Collections.Generic.List[object]'
while($clock.Elapsed.TotalSeconds -lt 65){
 $probe=[Diagnostics.Stopwatch]::StartNew()
 $responsive=[RecoveryResponseProbe]::Responds(922936)
 $rows.Add([pscustomobject]@{ElapsedMs=$clock.ElapsedMilliseconds;Responsive=$responsive;ResponseMs=$probe.ElapsedMilliseconds})
 Start-Sleep -Milliseconds 100
}
$after=Invoke-RestMethod 'http://127.0.0.1:7788/api/ear/health' -TimeoutSec 2
$workers=@(Get-CimInstance Win32_Process -Filter "Name='chrome.exe'"|Where-Object {$_.CommandLine -like '*C:\자비스\data\chrome-ear-profile*' -and $_.CommandLine -notlike '*--type=*'})
$hud.Refresh()
$summary=[ordered]@{Test='Actual dedicated microphone worker interruption and recovery';HudPid=$hud.Id;OriginalWorkerPid=$worker.ProcessId;FinalWorkerPids=@($workers.ProcessId);BeforeConnected=$before.connected;AfterConnected=$after.connected;HudStillRunning=(-not $hud.HasExited);Samples=$rows.Count;Failures=@($rows|Where-Object {-not $_.Responsive});MaxResponseMs=($rows.ResponseMs|Measure-Object -Maximum).Maximum;Passed=((-not $hud.HasExited) -and $after.connected -and $workers.Count -eq 1 -and $workers[0].ProcessId -ne $worker.ProcessId -and @($rows|Where-Object {-not $_.Responsive}).Count -eq 0);Trace=$rows}
$summary|ConvertTo-Json -Depth 5|Set-Content -LiteralPath 'C:\자비스\data\verification\2026-10-30-microphone-recovery-ui.json' -Encoding UTF8
$summary.Remove('Trace')
$summary|ConvertTo-Json -Depth 5 -Compress
if(-not $summary.Passed){exit 1}
