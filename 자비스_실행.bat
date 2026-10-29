@echo off
start "" powershell.exe -NoProfile -WindowStyle Hidden -Command "Start-Process -FilePath 'node.exe' -ArgumentList ('C:\' + [char]0xC790 + [char]0xBE44 + [char]0xC2A4 + '\server.js') -WorkingDirectory ('C:\' + [char]0xC790 + [char]0xBE44 + [char]0xC2A4) -WindowStyle Hidden"
start "" powershell.exe -NoProfile -STA -Command "Add-Type -TypeDefinition ([System.IO.File]::ReadAllText(('C:\' + [char]0xC790 + [char]0xBE44 + [char]0xC2A4 + '\scripts\JarvisMonitorOverlay.cs'), [System.Text.Encoding]::UTF8)) -Language CSharp -ReferencedAssemblies PresentationFramework,PresentationCore,WindowsBase,System.Xaml,System.Drawing,System.Windows.Forms,System.Speech,System.Net.Http; [JarvisSasariSa.MonitorOverlayApp]::RunOverlay()"
exit
