param([switch]$StartIfStopped)
$ErrorActionPreference = 'Stop'
Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Text;
using System.Runtime.InteropServices;
public class JarvisVisibleWindow { public string Title; public bool Primary; }
public static class JarvisStartupInventory {
  private delegate bool Callback(IntPtr window, IntPtr data);
  [StructLayout(LayoutKind.Sequential)] private struct RECT { public int L,T,R,B; }
  [StructLayout(LayoutKind.Sequential)] private struct MONITORINFO { public int Size; public RECT Monitor,Work; public uint Flags; }
  [DllImport("user32.dll")] private static extern bool EnumWindows(Callback callback, IntPtr data);
  [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
  [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] private static extern int GetWindowText(IntPtr window, StringBuilder text, int capacity);
  [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);
  [DllImport("user32.dll")] private static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);
  public static JarvisVisibleWindow[] Windows(uint processId) {
    var windows=new List<JarvisVisibleWindow>();
    Callback callback=(window,data)=> {
      uint owner; GetWindowThreadProcessId(window,out owner);
      if(owner==processId && IsWindowVisible(window)) {
        var title=new StringBuilder(512); GetWindowText(window,title,title.Capacity);
        if(title.Length>0) {
          var info=new MONITORINFO(); info.Size=Marshal.SizeOf(typeof(MONITORINFO));
          bool primary=GetMonitorInfo(MonitorFromWindow(window,2),ref info) && (info.Flags & 1)!=0;
          windows.Add(new JarvisVisibleWindow { Title=title.ToString(), Primary=primary });
        }
      }
      return true;
    };
    EnumWindows(callback,IntPtr.Zero); return windows.ToArray();
  }
}
'@
$project = Split-Path -Parent $PSScriptRoot
$binary = Join-Path $project 'bin\JarvisHud.exe'
$next = Join-Path $project 'bin\JarvisHud.next.exe'
if (-not (Test-Path -LiteralPath $next -PathType Leaf)) { throw 'Staged HUD is missing.' }
$prefix = $project.TrimEnd('\') + '\'
$existing = @(Get-CimInstance Win32_Process -Filter "Name LIKE 'JarvisHud%'" | Where-Object { $_.ExecutablePath -and $_.ExecutablePath.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase) })
if ($StartIfStopped) {
  $allHuds = @(Get-CimInstance Win32_Process -Filter "Name LIKE 'JarvisHud%'")
  if ($allHuds.Count) { throw 'A HUD is already running. StartIfStopped never terminates existing processes.' }
} else {
  foreach ($item in $existing) { Stop-Process -Id $item.ProcessId; Wait-Process -Id $item.ProcessId -ErrorAction SilentlyContinue }
}
Start-Sleep -Seconds 1
Copy-Item -LiteralPath $next -Destination $binary -Force
Copy-Item -LiteralPath $next -Destination (Join-Path $project 'JarvisHud.exe') -Force
$process = Start-Process -FilePath $binary -WorkingDirectory $project -WindowStyle Hidden -PassThru
$clock = [Diagnostics.Stopwatch]::StartNew()
$seenBoot = $false; $bootOnPrimary = $true; $hudOnPrimary = $true; $overlap = $false; $hudBeforeBoot = $false; $maxHud = 0
$firstBootMs = $null; $firstHudMs = $null; $finalHud = @(); $finalBoot = @()
while ($clock.Elapsed.TotalSeconds -lt 10) {
  $windows = @([JarvisStartupInventory]::Windows([uint32]$process.Id))
  $boot = @($windows | Where-Object { $_.Title -like '*BootScene Overlay' })
  $hud = @($windows | Where-Object { $_.Title -match 'Monitor.*HUD' })
  if ($boot.Count) { if (-not $seenBoot) { $firstBootMs = $clock.ElapsedMilliseconds }; $seenBoot = $true }
  if ($hud.Count -and $null -eq $firstHudMs) { $firstHudMs = $clock.ElapsedMilliseconds }
  if ($hud.Count -and -not $seenBoot) { $hudBeforeBoot = $true }
  if ($boot.Count -and $hud.Count) { $overlap = $true }
  if (@($boot | Where-Object { -not $_.Primary }).Count) { $bootOnPrimary = $false }
  if (@($hud | Where-Object { -not $_.Primary }).Count) { $hudOnPrimary = $false }
  $maxHud = [Math]::Max($maxHud, $hud.Count)
  $finalHud = $hud; $finalBoot = $boot
  Start-Sleep -Milliseconds 100
}
$passed = $seenBoot -and $bootOnPrimary -and $hudOnPrimary -and -not $overlap -and -not $hudBeforeBoot -and $maxHud -eq 1 -and $finalHud.Count -eq 1 -and $finalBoot.Count -eq 0
[pscustomobject]@{ Passed=$passed; ProcessId=$process.Id; BootObserved=$seenBoot; FirstBootMs=$firstBootMs; FirstHudMs=$firstHudMs; BootOnPrimary=$bootOnPrimary; HudOnPrimary=$hudOnPrimary; BootHudOverlap=$overlap; MaximumHudCount=$maxHud; FinalHudCount=$finalHud.Count; FinalBootCount=$finalBoot.Count } | ConvertTo-Json -Compress
if (-not $passed) { exit 1 }
