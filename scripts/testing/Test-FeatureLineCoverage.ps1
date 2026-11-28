$ErrorActionPreference = 'Stop'
$root = Join-Path ([IO.Path]::GetTempPath()) ('mhw-feature-lines-' + [Guid]::NewGuid().ToString('N'))
$reader = Join-Path $PSScriptRoot 'Get-FeatureLineCoverage.ps1'
try {
    foreach ($folder in @('src/A','src/B','reports')) { New-Item -ItemType Directory -Path (Join-Path $root $folder) -Force | Out-Null }
    foreach ($file in @('src/A/Engine.cs','src/B/Engine.cs','src/A/Layout.xaml')) {
        Set-Content -LiteralPath (Join-Path $root $file) -Value @('one','two','three','four') -Encoding UTF8
    }
    $matrix = Join-Path $root 'matrix.json'
    $features = @(
        @{id='alpha';risk='critical';productionPaths=@('src/A/Engine.cs','src/A/Layout.xaml')},
        @{id='beta';risk='important';productionPaths=@('src/A/Engine.cs','src/B/Engine.cs')}
    )
    ConvertTo-Json -InputObject @{schemaVersion=1;features=$features} -Depth 6 | Set-Content -LiteralPath $matrix -Encoding UTF8
    $a = Join-Path $root 'reports/unit.xml'
    $b = Join-Path $root 'reports/integration.xml'
    $escapedRoot = [Security.SecurityElement]::Escape($root)
    Set-Content -LiteralPath $a -Encoding UTF8 -Value @"
<coverage><sources><source>$escapedRoot</source></sources><packages><package><classes>
<class filename="src/A/Engine.cs"><lines><line number="1" hits="1"/><line number="2" hits="0"/></lines></class>
<class filename="src/A/Engine.cs"><lines><line number="1" hits="2"/></lines></class>
<class filename="src/B/Engine.cs"><lines><line number="1" hits="0"/></lines></class>
<class filename="Engine.cs"><lines><line number="1" hits="5"/></lines></class>
</classes></package></packages></coverage>
"@
    Set-Content -LiteralPath $b -Encoding UTF8 -Value @"
<coverage><sources><source>$escapedRoot/src/A</source></sources><packages><package><classes>
<class filename="Engine.cs"><lines><line number="2" hits="1"/><line number="3" hits="0"/></lines></class>
<class filename="/_/src/B/Engine.cs"><lines><line number="1" hits="1"/></lines></class>
<class filename="https://example.invalid/src/A/Engine.cs"><lines><line number="3" hits="99"/></lines></class>
</classes></package></packages></coverage>
"@
    $sha = 'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa'
    $result = & $reader -RepositoryRoot $root -MatrixPath $matrix -ReportPaths @($a,$b) -SourceSha $sha -Suite All
    if ($result.sourceSha -cne $sha -or $result.reportCount -ne 2 -or
        $result.mappedFiles -ne 2 -or $result.mappedLines -ne 4 -or $result.coveredMappedLines -ne 3 -or
        $result.unmappedClassCount -ne 2) {
        throw ('Incorrect union or provenance: ' + ($result | ConvertTo-Json -Depth 10 -Compress))
    }
    $alpha = @($result.features | Where-Object { $_.id -eq 'alpha' })[0]
    $beta = @($result.features | Where-Object { $_.id -eq 'beta' })[0]
    if ($alpha.lines -ne 3 -or $alpha.coveredLines -ne 2 -or $alpha.uninstrumentedPaths.Count -ne 1 -or
        $alpha.completePathMeasurement -or $beta.lines -ne 4 -or $beta.coveredLines -ne 3 -or
        !$beta.completePathMeasurement -or $result.sharedMappedPaths.Count -ne 1) {
        throw ('Feature mapping failed: ' + ($result | ConvertTo-Json -Depth 10 -Compress))
    }
    # A conflicting declared source root must not be resolved by basename.
    Set-Content -LiteralPath $b -Encoding UTF8 -Value @"
<coverage><sources><source>$escapedRoot/src/A</source><source>$escapedRoot/src/B</source></sources><packages><package><classes>
<class filename="Engine.cs"><lines><line number="1" hits="1"/></lines></class>
</classes></package></packages></coverage>
"@
    $ambiguous = & $reader -RepositoryRoot $root -MatrixPath $matrix -ReportPaths @($b) -SourceSha $sha -Suite Integration
    if ($ambiguous.ambiguousClassCount -ne 1 -or $ambiguous.mappedLines -ne 0 -or
        $null -ne $ambiguous.features[0].linePercent) {
        throw ('Ambiguous mapping was credited: ' + ($ambiguous | ConvertTo-Json -Depth 10 -Compress))
    }
    Set-Content -LiteralPath $b -Encoding UTF8 -Value '<coverage><packages><package><classes><class filename="src/A/Engine.cs"><lines><line number="0" hits="1"/></lines></class></classes></package></packages></coverage>'
    $rejected = $false
    try { & $reader -RepositoryRoot $root -MatrixPath $matrix -ReportPaths @($b) -SourceSha $sha -Suite Integration | Out-Null } catch { $rejected = $true }
    if (!$rejected) { throw 'Invalid line number was credited.' }
    $rejected = $false
    try { & $reader -RepositoryRoot $root -MatrixPath $matrix -ReportPaths @($a,$a) -SourceSha $sha -Suite All | Out-Null } catch { $rejected = $true }
    if (!$rejected) { throw 'Duplicate report identity was accepted.' }
    Write-Host 'PASS: Feature line evidence deduplicates source lines, separates missing paths and rejects ambiguous or invalid evidence.'
}
finally {
    if (Test-Path -LiteralPath $root) { Remove-Item -LiteralPath $root -Recurse -Force }
}
