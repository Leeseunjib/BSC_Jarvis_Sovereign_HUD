param([switch]$Install)
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.Encoding]::UTF8
$project = Split-Path -Parent $PSScriptRoot
Push-Location $project
try {
    foreach ($test in @('test-speech-turn.js','test-speech-client.js','test-speech-replacement.js','test-speech-cancellation.js','test-media-control.js','test-safety.js')) {
        & node (Join-Path $PSScriptRoot $test)
        if ($LASTEXITCODE -ne 0) { throw "Check failed: $test" }
    }
    & (Join-Path $PSScriptRoot 'Build-VerifiedHud.ps1')
    if ($Install) { & (Join-Path $PSScriptRoot 'Install-VerifiedRuntime.ps1') }
    else { Write-Output 'Checks and candidate build passed. Use -Install for explicit local installation.' }
} finally { Pop-Location }
