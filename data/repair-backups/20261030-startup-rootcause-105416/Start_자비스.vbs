Set WshShell = CreateObject("WScript.Shell")
rootDir = "C:\" & ChrW(51088) & ChrW(48708) & ChrW(49828)
csFile = rootDir & "\scripts\JarvisMonitorOverlay.cs"
srvFile = rootDir & "\server.js"
WshShell.CurrentDirectory = rootDir
WshShell.Run "node.exe """ & srvFile & """", 0, False
WScript.Sleep 900
psCmd = "powershell.exe -NoProfile -STA -Command ""Add-Type -TypeDefinition ([System.IO.File]::ReadAllText('" & csFile & "', [System.Text.Encoding]::UTF8)) -Language CSharp -ReferencedAssemblies PresentationFramework,PresentationCore,WindowsBase,System.Xaml,System.Drawing,System.Windows.Forms,System.Speech,System.Net.Http; [JarvisSasariSa.MonitorOverlayApp]::RunOverlay()"""
WshShell.Run psCmd, 0, False
