$ErrorActionPreference = "Stop"
$csPath = "C:\자비스\scripts\JarvisMonitorOverlay.cs"
$code = [System.IO.File]::ReadAllText($csPath, [System.Text.Encoding]::UTF8)

Add-Type -TypeDefinition $code -Language CSharp -ReferencedAssemblies @(
    "PresentationFramework",
    "PresentationCore",
    "WindowsBase",
    "System.Xaml",
    "System.Drawing",
    "System.Windows.Forms",
    "System.Speech",
    "System.Net.Http"
)

[JarvisSasariSa.MonitorOverlayApp]::RunOverlay()
