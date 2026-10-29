$ErrorActionPreference = 'Stop'
Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Text;
using System.Runtime.InteropServices;
public class JarvisHudPlacement { public uint ProcessId; public bool Primary; public bool Topmost; }
public static class JarvisWindowInventory {
  private delegate bool Callback(IntPtr window, IntPtr data);
  [DllImport("user32.dll")] private static extern bool EnumWindows(Callback callback, IntPtr data);
  [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
  [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
  [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr window);
  [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);
  [StructLayout(LayoutKind.Sequential)] private struct RECT { public int L,T,R,B; }
  [StructLayout(LayoutKind.Sequential)] private struct MONITORINFO { public int Size; public RECT Monitor,Work; public uint Flags; }
  [DllImport("user32.dll")] private static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);
  [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr window, int index);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] private static extern int GetWindowText(IntPtr window, StringBuilder text, int capacity);
  public static string[] VisibleTitles(uint processId) {
    var titles = new List<string>();
    Callback callback = (window, data) => {
      uint owner; GetWindowThreadProcessId(window, out owner);
      if(owner == processId && IsWindowVisible(window)) {
        var title = new StringBuilder(512); GetWindowText(window,title,title.Capacity);
        if(title.Length > 0) titles.Add(title.ToString());
      }
      return true;
    };
    EnumWindows(callback, IntPtr.Zero);
    return titles.ToArray();
  }
  public static string[] OnscreenHudWindows() {
    var titles = new List<string>();
    Callback callback = (window, data) => {
      if (!IsWindowVisible(window) || IsIconic(window) || MonitorFromWindow(window,0)==IntPtr.Zero) return true;
      var text = new StringBuilder(512); GetWindowText(window,text,text.Capacity);
      string title = text.ToString();
      if ((title.StartsWith("\uc790\ube44\uc2a4") || title.StartsWith("Jarvis",StringComparison.OrdinalIgnoreCase)) && title.Contains("HUD")) {
        uint owner; GetWindowThreadProcessId(window,out owner);
        titles.Add(owner + "|" + title);
      }
      return true;
    };
    EnumWindows(callback,IntPtr.Zero);
    return titles.ToArray();
  }
  public static JarvisHudPlacement[] Placements(uint processId) {
    var found = new List<JarvisHudPlacement>();
    Callback callback = (window, data) => {
      uint owner; GetWindowThreadProcessId(window,out owner);
      var title = new StringBuilder(512); GetWindowText(window,title,title.Capacity);
      if (owner==processId && IsWindowVisible(window) && !IsIconic(window) && title.ToString().Contains("Monitor") && title.ToString().Contains("HUD")) {
        var info = new MONITORINFO(); info.Size=Marshal.SizeOf(typeof(MONITORINFO));
        found.Add(new JarvisHudPlacement { ProcessId=owner, Primary=GetMonitorInfo(MonitorFromWindow(window,0),ref info) && (info.Flags & 1)!=0, Topmost=(GetWindowLong(window,-20) & 8)!=0 });
      }
      return true;
    };
    EnumWindows(callback,IntPtr.Zero); return found.ToArray();
  }
}
'@
$project = Split-Path -Parent $PSScriptRoot
$prefix = $project.TrimEnd('\') + '\'
$items = @(Get-CimInstance Win32_Process -Filter "Name LIKE 'JarvisHud%'" | Where-Object { $_.ExecutablePath -and $_.ExecutablePath.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase) })
$windows = @()
foreach ($item in $items) { $windows += @([JarvisWindowInventory]::VisibleTitles([uint32]$item.ProcessId) | Where-Object { $_ -match 'Monitor.*HUD' }) }
$global = @([JarvisWindowInventory]::OnscreenHudWindows())
$placements = @($items | ForEach-Object { [JarvisWindowInventory]::Placements([uint32]$_.ProcessId) })
$primaryTopmost = $placements.Count -eq 1 -and $placements[0].Primary -and $placements[0].Topmost
[pscustomobject]@{ ProcessCount = $items.Count; VisibleHudCount = $windows.Count; GlobalOnscreenHudCount = $global.Count; PrimaryAndTopmost = $primaryTopmost; Titles = @($windows); OnscreenHuds = $global } | ConvertTo-Json -Compress
if ($items.Count -ne 1 -or $windows.Count -ne 1 -or $global.Count -ne 1 -or -not $primaryTopmost) { exit 1 }
