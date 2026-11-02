param([string]$OutputName = 'JarvisHud.next.exe')
$ErrorActionPreference = 'Stop'
$project = Split-Path -Parent $PSScriptRoot
$source = Join-Path $PSScriptRoot 'JarvisMonitorOverlay.cs'
if ([IO.Path]::GetFileName($OutputName) -ne $OutputName) { throw 'OutputName must be a file name.' }
$output = Join-Path $project ('bin\' + $OutputName)
$framework = Join-Path $env:SystemRoot 'Microsoft.NET\Framework64\v4.0.30319'
$speech = Get-Item (Join-Path $env:SystemRoot 'Microsoft.NET\assembly\GAC_MSIL\System.Speech\*\System.Speech.dll') | Select-Object -First 1 -ExpandProperty FullName
$references = @("$framework\WPF\PresentationFramework.dll", "$framework\WPF\PresentationCore.dll", "$framework\WPF\WindowsBase.dll", "$framework\System.Xaml.dll", "$framework\System.Drawing.dll", "$framework\System.Windows.Forms.dll", "$framework\System.Management.dll", $speech, "$framework\System.Net.Http.dll")
$references += "$framework\System.Web.Extensions.dll"
$hash = (Get-FileHash -LiteralPath $source).Hash
$compilerArgs = @('/nologo', '/target:winexe', ('/out:' + $output)) + @($references | ForEach-Object { '/reference:' + $_ }) + @($source)
& "$framework\csc.exe" @compilerArgs
if ($LASTEXITCODE -ne 0) { throw 'HUD compilation failed.' }
if ((Get-FileHash -LiteralPath $source).Hash -ne $hash) { throw 'HUD source changed during compilation.' }
Copy-Item -LiteralPath $source -Destination (Join-Path $PSScriptRoot 'verified-hud\MonitorOverlayApp.cs') -Force
@{ source = 'scripts/JarvisMonitorOverlay.cs'; sourceHash = $hash; binary = $OutputName; binaryHash = (Get-FileHash -LiteralPath $output).Hash; builtAt = [DateTime]::UtcNow.ToString('o') } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $project 'bin\hud-build.json') -Encoding UTF8
Write-Output $output
