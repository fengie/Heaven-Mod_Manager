param(
    [string]$Filter = '*',
    [ValidateSet('Dry', 'Short', 'Medium', 'Long')]
    [string]$Job = 'Short'
)
$ErrorActionPreference = 'Stop'
$Root = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
. (Join-Path $PSScriptRoot '..\diagnostics\Master-Debug.ps1')
Start-MhwMasterDebugSession -Root $Root -Area 'BENCHMARK' -Title 'Subsystem benchmark run'
Push-Location $Root
$ok = $false
try {
    $args = @(
        'run', '-c', 'Release', '--project',
        '.\benchmarks\MhwModManager.Benchmarks\MhwModManager.Benchmarks.csproj',
        '--', '--filter', $Filter, '--job', $Job, '--exporters', 'json'
    )
    & dotnet @args 2>&1 | ForEach-Object {
        $line = $_.ToString()
        Write-Host $line
        Write-MhwMasterDebug -Root $Root -Area 'DOTNET' -Message ('[Benchmark] ' + $line)
    }
    if ($LASTEXITCODE -ne 0) { throw "Benchmark run failed with exit code $LASTEXITCODE" }
    $ok = $true
}
catch {
    Write-MhwMasterDebug -Root $Root -Area 'BENCHMARK-FATAL' -Message $_.Exception.ToString()
    throw
}
finally {
    Pop-Location
    Stop-MhwMasterDebugSession -Root $Root -Area 'BENCHMARK' -Summary $(if ($ok) { 'PASS' } else { 'FAIL' })
}
