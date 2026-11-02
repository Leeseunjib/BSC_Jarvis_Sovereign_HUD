param(
    [Parameter(Mandatory=$true)][int]$HudProcessId,
    [Parameter(Mandatory=$true)][long]$ExpectedStartTimeTicks
)
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.Encoding]::UTF8
$project = Split-Path -Parent $PSScriptRoot
$installed = Join-Path $project 'bin\JarvisHud.exe'
$rootCopy = Join-Path $project 'JarvisHud.exe'
$log = Join-Path $project 'data\hud-supervisor.log'
function Write-SupervisorLog([string]$Message) {
    try { Add-Content -LiteralPath $log -Value ((Get-Date -Format o) + ' ' + $Message) -Encoding UTF8 } catch { }
}
$created = $false
$guard = New-Object Threading.Mutex($true, 'Local\JarvisHudSupervisor', [ref]$created)
if (-not $created) { $guard.Dispose(); exit 0 }
$stop = New-Object Threading.EventWaitHandle($false, [Threading.EventResetMode]::ManualReset, 'Local\JarvisHudSupervisorStop')
$watched = $null
try {
    # Pin both identity and path. Never adopt a reused PID or an unrelated app.
    $watched = [Diagnostics.Process]::GetProcessById($HudProcessId)
    if ($watched.StartTime.ToUniversalTime().Ticks -ne $ExpectedStartTimeTicks) { throw 'HUD process identity changed.' }
    $executable = $watched.MainModule.FileName
    if ($executable -ine $installed -and $executable -ine $rootCopy) { throw 'Only the installed project HUD may be supervised.' }
    $null = $watched.Handle # Retain a handle so exit status remains available after process removal.
    Write-SupervisorLog ('Watching HUD pid=' + $watched.Id)
    $rapidExits = 0
    while (-not $stop.WaitOne(0)) {
        if (-not $watched.WaitForExit(1000)) { continue }
        if ($stop.WaitOne(0)) { break }
        $lifeSeconds = ([DateTime]::Now - $watched.StartTime).TotalSeconds
        $exitCode = $watched.ExitCode
        if ($null -eq $exitCode) { $exitCode = 'unavailable' }
        Write-SupervisorLog ('Unexpected exit pid=' + $watched.Id + ' code=' + $exitCode + ' lifetimeSeconds=' + [math]::Round($lifeSeconds,1))
        $watched.Dispose(); $watched = $null
        if ($lifeSeconds -lt 30) { $rapidExits++ } else { $rapidExits=0 }
        # Back off repeated failures instead of creating a rapid relaunch loop.
        $delayMs = [int][math]::Min(30000, 1000 * [math]::Pow(2, [math]::Min($rapidExits,5)))
        if ($stop.WaitOne($delayMs)) { break }
        while (-not $stop.WaitOne(0)) {
            try {
                # A desktop click may already have reopened the HUD during the delay.
                $existing = @([Diagnostics.Process]::GetProcessesByName('JarvisHud') | Where-Object {
                    try { -not $_.HasExited -and ($_.MainModule.FileName -ieq $installed -or $_.MainModule.FileName -ieq $rootCopy) } catch { $false }
                })
                if ($existing.Count -eq 1) {
                    $watched = $existing[0]
                    Write-SupervisorLog ('Adopted reopened HUD pid=' + $watched.Id)
                } elseif ($existing.Count -gt 1) {
                    foreach ($item in $existing) { $item.Dispose() }
                    throw 'More than one installed HUD process is still settling.'
                } else {
                    $watched = Start-Process -FilePath $installed -WorkingDirectory $project -WindowStyle Hidden -PassThru
                    Write-SupervisorLog ('Recovered HUD pid=' + $watched.Id)
                }
                $null = $watched.Handle
                break
            } catch {
                Write-SupervisorLog ('Relaunch failed: ' + $_.Exception.Message)
                if ($stop.WaitOne(30000)) { break }
            }
        }
    }
    Write-SupervisorLog 'Supervisor stopped by explicit quit or deployment.'
} catch {
    Write-SupervisorLog ('Supervisor error: ' + $_.Exception.Message)
    exit 1
} finally {
    if ($null -ne $watched) { $watched.Dispose() }
    $stop.Dispose()
    $guard.ReleaseMutex(); $guard.Dispose()
}
