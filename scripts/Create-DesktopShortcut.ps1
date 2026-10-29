$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$project = Split-Path -Parent $PSScriptRoot
$desktop = [Environment]::GetFolderPath('Desktop')
if (-not $desktop -or -not (Test-Path -LiteralPath $desktop -PathType Container)) { throw '바탕화면 폴더를 찾을 수 없습니다.' }
$shortcutPath = Join-Path $desktop '자비스 HUD.lnk'
$launcher = Join-Path $PSScriptRoot 'Open-JarvisHud.ps1'
$icon = Join-Path $project 'JarvisHud.exe'
if (-not (Test-Path -LiteralPath $launcher) -or -not (Test-Path -LiteralPath $icon)) { throw '자비스 실행 파일을 찾을 수 없습니다.' }
$shell = New-Object -ComObject WScript.Shell
$link = $shell.CreateShortcut($shortcutPath)
$link.TargetPath = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
$link.Arguments = '-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File "' + $launcher + '"'
$link.WorkingDirectory = $project
$link.IconLocation = $icon + ',0'
$link.Description = '자비스 HUD 열기'
$link.Save()
Write-Output $shortcutPath
