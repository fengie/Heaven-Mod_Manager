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
# Authoritative measurement binds the entire invocation to a clean checkout.
# Generated evidence must be under ignored output or outside the checkout.
function Read-ExactCoverageSource {
    $sha = (& git -C $repo rev-parse HEAD)
    if ($LASTEXITCODE -ne 0 -or ([string]$sha) -notmatch '^[0-9a-fA-F]{40}$') {
        throw 'Cannot identify the exact coverage source revision.'
    }
    $dirty = @(& git -C $repo status --porcelain --untracked-files=normal)
    if ($LASTEXITCODE -ne 0 -or $dirty.Count -gt 0) {
        throw 'Coverage measurement requires a clean repository checkout and ignored or external evidence output.'
    }
    return ([string]$sha).ToLowerInvariant()
}
$sourceSha = Read-ExactCoverageSource
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
if ((Read-ExactCoverageSource) -cne $sourceSha) { throw 'Coverage source changed during collection.' }
$reportPaths = [string[]]@($summary | ForEach-Object { $_.report })
$featureEvidence = & (Join-Path $PSScriptRoot 'Get-FeatureLineCoverage.ps1') `
    -RepositoryRoot $repo -MatrixPath (Join-Path $repo 'docs/verification/feature-test-matrix.json') `
    -ReportPaths $reportPaths -SourceSha $sourceSha -Suite $Suite
$featurePath = Join-Path $invocation 'feature-line-coverage.json'
$featureEvidence | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $featurePath -Encoding UTF8
Write-Host ("FEATURE LINE EVIDENCE: {0} ({1}/{2} mapped lines covered, {3}/{4} mapped files instrumented)" -f `
    $featurePath,$featureEvidence.coveredMappedLines,$featureEvidence.mappedLines,$featureEvidence.mappedFiles,$featureEvidence.registeredMappedFiles)
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
    $lines += ''
    $lines += '## Feature-mapped source line evidence (deduplicated)'
    $lines += "Exact source: $sourceSha; suites: $Suite; reports: $($featureEvidence.reportCount)."
    $lines += '| Feature | Covered lines | Instrumented lines | Line % | Uninstrumented mapped paths |'
    $lines += '|---|---:|---:|---:|---:|'
    foreach ($feature in $featureEvidence.features) {
        $percentage = if ($null -ne $feature.linePercent) { "$($feature.linePercent)%" } else { 'Not measured' }
        $lines += "| $($feature.id) | $($feature.coveredLines) | $($feature.lines) | $percentage | $($feature.uninstrumentedPaths.Count) |"
    }
    $lines += ''
    $lines += "Unique mapped source lines: $($featureEvidence.coveredMappedLines)/$($featureEvidence.mappedLines); unmapped classes: $($featureEvidence.unmappedClassCount); ambiguous classes: $($featureEvidence.ambiguousClassCount)."
    $lines += 'Feature scopes can overlap. Uninstrumented paths remain explicitly unknown; no merged branch, whole-repository or installed-client coverage is asserted.'
    $lines | Out-File -LiteralPath $env:GITHUB_STEP_SUMMARY -Encoding UTF8 -Append
}
