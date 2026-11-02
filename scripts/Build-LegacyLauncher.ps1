$ErrorActionPreference='Stop'
[Console]::OutputEncoding=[Text.Encoding]::UTF8
$project=Split-Path -Parent $PSScriptRoot
$source=Join-Path $PSScriptRoot 'JarvisLegacyLauncher.cs'
$candidate=Join-Path $project 'bin\JarvisLegacyLauncher.next.exe'
$target=Join-Path $project 'JarvisHud_v20.exe'
$framework=Join-Path $env:SystemRoot 'Microsoft.NET\Framework64\v4.0.30319'
& (Join-Path $framework 'csc.exe') /nologo /target:winexe ('/out:'+$candidate) ('/reference:'+(Join-Path $framework 'System.Windows.Forms.dll')) $source
if($LASTEXITCODE -ne 0){throw 'Compatibility launcher compilation failed.'}
if(Test-Path -LiteralPath $target){
 $backup=Join-Path $project ('data\repair-backups\legacy-launcher-'+(Get-Date -Format 'yyyyMMdd-HHmmss'))
 New-Item -ItemType Directory -Path $backup -Force|Out-Null
 Copy-Item -LiteralPath $target -Destination (Join-Path $backup 'JarvisHud_v20.exe.disabled')
}
Copy-Item -LiteralPath $candidate -Destination $target -Force
if((Get-FileHash -LiteralPath $candidate).Hash -ne (Get-FileHash -LiteralPath $target).Hash){throw 'Compatibility launcher verification failed.'}
Write-Output 'Old v20 entry point now forwards to the installed single-instance HUD.'
