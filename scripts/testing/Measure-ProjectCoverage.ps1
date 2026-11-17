# Opt-in .NET 10 Microsoft.Testing.Platform code coverage on isolated test-owned output.
# Invocation: .\scripts\testing\Measure-ProjectCoverage.ps1 [-Suite All|Unit|Automation|Integration]
param(
    [ValidateSet('All','Unit','Automation','Integration')]
    [string]$Suite = 'All',
    [string]$OutputRoot = 'BuildLogs\Coverage'
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$startedAt = [DateTimeOffset]::UtcNow
$suites = @(
    @{ Name = 'Unit'; Path = 'tests\MhwModManager.Tests\MhwModManager.Tests.csproj' },
    @{ Name = 'Automation'; Path = 'tests\MhwModManager.AutomationTests\MhwModManager.AutomationTests.csproj' },
    @{ Name = 'Integration'; Path = 'tests\MhwModManager.IntegrationTests\MhwModManager.IntegrationTests.csproj' }
)
$selected = @($suites | Where-Object { $Suite -eq 'All' -or $_.Name -eq $Suite })
if ($selected.Count -eq 0) { throw "No test project selected." }

$root = if ([IO.Path]::IsPathRooted($OutputRoot)) { [IO.Path]::GetFullPath($OutputRoot) }
    else { [IO.Path]::GetFullPath((Join-Path $repo $OutputRoot)) }
$invocation = Join-Path $root ("run-" + $startedAt.ToString('yyyyMMdd-HHmmss-fff'))
New-Item -ItemType Directory -Path $invocation -Force | Out-Null
$summary = New-Object 'System.Collections.Generic.List[object]'
$oldDir = Get-Location
try {
    Set-Location $repo
    foreach ($entry in $selected) {
        $suiteDir = Join-Path $invocation $entry.Name
        New-Item -ItemType Directory -Path $suiteDir -Force | Out-Null
        $args = @('test', $entry.Path, '-c', 'Release',
            '--results-directory', $suiteDir,
            '--coverage', '--coverage-output-format', 'cobertura')
        Write-Host ("COVERAGE START suite={0} project={1}" -f $entry.Name,$entry.Path)
        & dotnet @args
        if ($LASTEXITCODE -ne 0) {
            throw ("Coverage test suite {0} failed (exit={1}). No passing coverage report claimed." -f $entry.Name,$LASTEXITCODE)
        }
        $reports = @(Get-ChildItem -LiteralPath $suiteDir -Recurse -File -Filter '*.cobertura.xml')
        if ($reports.Count -eq 0) { throw "Coverage was requested but produced no Cobertura XML for $($entry.Name)." }
        foreach ($report in $reports) {
            [xml]$document = Get-Content -LiteralPath $report.FullName -Raw
            $node = $document.SelectSingleNode('/coverage')
            if ($null -eq $node) { throw "Malformed Cobertura report: $($report.FullName)" }
            foreach ($key in @('lines-valid','lines-covered','branches-valid','branches-covered')) {
                if (-not $node.HasAttribute($key)) { throw "Cobertura report missing $key in $($report.FullName)" }
            }
            $linesValid = [int]::Parse($node.GetAttribute('lines-valid'),[Globalization.CultureInfo]::InvariantCulture)
            $linesCovered = [int]::Parse($node.GetAttribute('lines-covered'),[Globalization.CultureInfo]::InvariantCulture)
            $branchesValid = [int]::Parse($node.GetAttribute('branches-valid'),[Globalization.CultureInfo]::InvariantCulture)
            $branchesCovered = [int]::Parse($node.GetAttribute('branches-covered'),[Globalization.CultureInfo]::InvariantCulture)
            if ($linesValid -le 0 -or $linesCovered -lt 0 -or $linesCovered -gt $linesValid -or
                $branchesValid -lt 0 -or $branchesCovered -lt 0 -or $branchesCovered -gt $branchesValid) {
                throw "Invalid coverage counters: $($report.FullName)"
            }
            $summary.Add([pscustomobject]@{
                suite = $entry.Name
                report = $report.FullName
                linesCovered = $linesCovered
                linesValid = $linesValid
                linePercent = [Math]::Round(100.0*$linesCovered/$linesValid,2)
                branchesCovered = $branchesCovered
                branchesValid = $branchesValid
                branchPercent = if ($branchesValid -gt 0) { [Math]::Round(100.0*$branchesCovered/$branchesValid,2) } else { $null }
            })
            Write-Host ("COVERAGE RESULT suite={0} line={1}/{2} branch={3}/{4} file={5}" -f
                $entry.Name,$linesCovered,$linesValid,$branchesCovered,$branchesValid,$report.FullName)
        }
    }
}
finally { Set-Location $oldDir }
$summaryPath = Join-Path $invocation 'coverage-summary.json'
@($summary.ToArray()) | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $summaryPath -Encoding UTF8
Write-Host "COVERAGE SUMMARY: $summaryPath"
# The same instrumented production assembly appears in multiple suites. Sum of
# per-suite line/branch counters is NOT overall combined coverage; do not invent it.
if (-not [string]::IsNullOrWhiteSpace($env:GITHUB_STEP_SUMMARY)) {
    $lines = @('## Per-suite measured code coverage','','Source: ' + $env:GITHUB_SHA,'',
        '| Suite | Covered lines | Lines | Line % | Covered branches | Branches |',
        '|---|---:|---:|---:|---:|---:|')
    foreach ($item in $summary) {
        $lines += "| $($item.suite) | $($item.linesCovered) | $($item.linesValid) | $($item.linePercent)% | $($item.branchesCovered) | $($item.branchesValid) |"
    }
    $lines += ''
    $lines += 'Not merged across projects; do not sum overlapping production assemblies. Benchmarks and installed WPF tests are separate.'
    $lines | Out-File -LiteralPath $env:GITHUB_STEP_SUMMARY -Encoding UTF8 -Append
}
