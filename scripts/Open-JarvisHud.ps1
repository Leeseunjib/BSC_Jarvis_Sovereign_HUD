$ErrorActionPreference = 'Stop'
$project = Split-Path -Parent $PSScriptRoot
$binExe = Join-Path $project 'bin\JarvisHud.exe'
if (-not (Test-Path -LiteralPath $binExe -PathType Leaf)) { throw 'The installed Jarvis HUD is missing. Build and install bin\JarvisHud.exe first.' }
# The HUD's named mutex handles concurrent clicks. Never replace a running build
# or import a scratch executable during ordinary startup.
Start-Process -FilePath $binExe -WorkingDirectory $project -WindowStyle Hidden
