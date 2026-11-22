$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '..\diagnostics\Read-StartupUiPhase.ps1')

function Assert-Rejects([scriptblock]$Action, [string]$CaseName) {
    $rejected = $false
    try {
        $null = & $Action
    }
    catch {
        if (-not $_.Exception.Message.Contains('Missing startup UI phase')) {
            throw "Wrong error for $CaseName : $($_.Exception.Message)"
        }
        $rejected = $true
    }
    if (-not $rejected) { throw "Parser accepted invalid input: $CaseName" }
}

$folder = Join-Path ([IO.Path]::GetTempPath()) ('umm-ui-phase-test-' + [guid]::NewGuid().ToString('N'))
New-Item -Path $folder -ItemType Directory -Force | Out-Null
$log = Join-Path $folder 'MHW-DEBUG-ALL.log'

try {
    $lines = @(
        '2026-11-18 14:00:00.000 -05:00 [STARTUP-UI-PHASE] [pid=12] [tid=1] phase=reload-mods; elapsedMs=123.45',
        '2026-11-18 14:00:00.100 -05:00 [OTHER] [pid=12] phase=conflict-analysis; elapsedMs=1.00',
        '2026-11-18 14:00:00.200 -05:00 [STARTUP-UI-PHASE] [pid=12] phase=conflict-analysis; elapsedMs=250.25',
        '2026-11-18 14:00:00.300 -05:00 [STARTUP-UI-PHASE] [pid=12] phase=reload-mods; elapsedMs=110.50'
    )
    Set-Content -LiteralPath $log -Value $lines -Encoding UTF8
    $reload = Read-StartupUiPhaseMs -LogPath $log -Phase 'reload-mods'
    $analysis = Read-StartupUiPhaseMs -LogPath $log -Phase 'conflict-analysis'
    if ([math]::Abs($reload - 110.50) -gt 0.01) {
        throw "Latest reload measurement was not selected: $reload"
    }
    if ([math]::Abs($analysis - 250.25) -gt 0.01) {
        throw "Conflict measurement was not selected: $analysis"
    }

    Assert-Rejects { Read-StartupUiPhaseMs -LogPath (Join-Path $folder 'absent.log') -Phase 'reload-mods' } 'absent log'
    Set-Content -LiteralPath $log -Value @(
        '2026-11-18 14:00:00.000 -05:00 [OTHER] phase=reload-mods; elapsedMs=12.00',
        '2026-11-18 14:00:01.000 -05:00 [STARTUP-UI-PHASE] phase=conflict-analysis; elapsedMs=NaN'
    ) -Encoding UTF8
    Assert-Rejects { Read-StartupUiPhaseMs -LogPath $log -Phase 'reload-mods' } 'wrong tag'
    Assert-Rejects { Read-StartupUiPhaseMs -LogPath $log -Phase 'conflict-analysis' } 'malformed numeric evidence'
    $current = Join-Path $folder 'startup.json'
    $next = Join-Path $folder 'startup.json.tmp'
    $old = Join-Path $folder 'previous.json'
    [IO.File]::WriteAllText($current, '{"overall":"RUNNING"}')
    [IO.File]::WriteAllText($next, '{"overall":"PASS"}')
    Read-StartupReportText $current | ForEach-Object {
        # This pipeline stage runs while the report reader is still open.
        [IO.File]::Replace($next, $current, $old)
        if (($_ | ConvertFrom-Json).overall -ne 'RUNNING') { throw 'The active reader lost its original snapshot.' }
    }
    if (((Read-StartupReportText $current) | ConvertFrom-Json).overall -ne 'PASS') {
        throw 'The terminal report was not visible to the next poll.'
    }
    Write-Host 'PASS: the startup report can be replaced during an active Windows reader.'
    Write-Host 'PASS: startup UI phase timings reject missing, spoofed, and malformed evidence.'
}
finally {
    Remove-Item -LiteralPath $folder -Recurse -Force -ErrorAction SilentlyContinue
}
