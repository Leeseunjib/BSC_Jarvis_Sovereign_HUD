param([switch]$NoLaunch)
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.Encoding]::UTF8
$project = Split-Path -Parent $PSScriptRoot
$meta = Get-Content -LiteralPath (Join-Path $project 'bin\hud-build.json') -Raw | ConvertFrom-Json
if ([IO.Path]::GetFileName($meta.binary) -ne $meta.binary) { throw 'Invalid candidate filename.' }
$candidate = Join-Path $project ('bin\' + $meta.binary)
if ((Get-FileHash -LiteralPath $candidate).Hash -ne $meta.binaryHash) { throw 'Candidate binary changed after build.' }
if ((Get-FileHash -LiteralPath (Join-Path $project $meta.source)).Hash -ne $meta.sourceHash) { throw 'HUD source changed after build. Rebuild first.' }
$installed = Join-Path $project 'bin\JarvisHud.exe'
$prefix = $project.TrimEnd('\') + '\'
$backup = Join-Path $project ('data\repair-backups\install-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $backup -Force | Out-Null
foreach ($file in @($installed, (Join-Path $project 'JarvisHud.exe'))) {
    if (Test-Path -LiteralPath $file) {
        $label = if ($file -eq $installed) { 'bin-JarvisHud.exe' } else { 'root-JarvisHud.exe' }
        Copy-Item -LiteralPath $file -Destination (Join-Path $backup $label)
    }
}
# Pause recovery before replacing the executable. The newly installed HUD starts it again.
try {
    $stop = [Threading.EventWaitHandle]::OpenExisting('Local\JarvisHudSupervisorStop')
    [void]$stop.Set(); $stop.Dispose()
    $deadline = [DateTime]::UtcNow.AddSeconds(5)
    do {
        $guard = $null
        if (-not [Threading.Mutex]::TryOpenExisting('Local\JarvisHudSupervisor', [ref]$guard)) { break }
        $guard.Dispose()
        if ([DateTime]::UtcNow -ge $deadline) { throw 'HUD supervisor did not stop; installation aborted.' }
        Start-Sleep -Milliseconds 100
    } while ($true)
} catch [Threading.WaitHandleCannotBeOpenedException] { }
# Stop only this project's HUD and exact Node entry points after validating the candidate.
$processes = @(Get-CimInstance Win32_Process)
foreach ($item in $processes) {
    $ourHud = $item.Name -like 'JarvisHud*.exe' -and $item.ExecutablePath -and $item.ExecutablePath.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)
    $ourServer = $item.Name -eq 'node.exe' -and ($item.CommandLine -match ([regex]::Escape($project) + '[\\/]server(?:\.verified)?\.js(?:["\s]|$)'))
    if ($ourHud -or $ourServer) {
        $owned = Get-Process -Id $item.ProcessId -ErrorAction SilentlyContinue
        if ($null -ne $owned) {
            try {
                Stop-Process -InputObject $owned -Force -ErrorAction Stop
                if (-not $owned.WaitForExit(8000)) { throw ('Process did not exit: ' + $item.ProcessId) }
            } finally { $owned.Dispose() }
        }
    }
}
Copy-Item -LiteralPath $candidate -Destination $installed -Force
Copy-Item -LiteralPath $candidate -Destination (Join-Path $project 'JarvisHud.exe') -Force
& node (Join-Path $PSScriptRoot 'Freeze-VerifiedRuntime.js')
if ($LASTEXITCODE -ne 0) { throw 'Runtime snapshot promotion failed.' }
if ((Get-FileHash -LiteralPath $installed).Hash -ne $meta.binaryHash) { throw 'Installed binary failed verification.' }
if (-not $NoLaunch) { Start-Process -FilePath $installed -WorkingDirectory $project -WindowStyle Hidden }
Write-Output 'Verified HUD and server installed from the reviewed sources.'
