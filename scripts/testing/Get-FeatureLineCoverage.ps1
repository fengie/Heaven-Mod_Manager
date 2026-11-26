[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$RepositoryRoot,
    [Parameter(Mandatory=$true)][string]$MatrixPath,
    [Parameter(Mandatory=$true)][string[]]$ReportPaths,
    [Parameter(Mandatory=$true)][ValidatePattern('^[0-9a-fA-F]{40}$')][string]$SourceSha,
    [Parameter(Mandatory=$true)][ValidateSet('All','Unit','Automation','Integration')][string]$Suite
)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath $RepositoryRoot).Path
$prefix = $root.Replace('\','/').TrimEnd('/') + '/'
$matrix = ConvertFrom-Json -InputObject (Get-Content -LiteralPath $MatrixPath -Raw -Encoding UTF8)
if ($matrix.schemaVersion -ne 1 -or @($matrix.features).Count -eq 0) { throw 'Invalid feature inventory.' }
$mapped = New-Object 'System.Collections.Generic.Dictionary[string,object]' -ArgumentList ([StringComparer]::OrdinalIgnoreCase)
$featureIds = New-Object 'System.Collections.Generic.HashSet[string]' -ArgumentList ([StringComparer]::OrdinalIgnoreCase)

foreach ($feature in $matrix.features) {
    if ([string]::IsNullOrWhiteSpace([string]$feature.id) -or !$featureIds.Add([string]$feature.id) -or
        @($feature.productionPaths).Count -eq 0) { throw 'Empty or duplicate feature identity, or missing source mapping.' }
    $unique = New-Object 'System.Collections.Generic.HashSet[string]' -ArgumentList ([StringComparer]::OrdinalIgnoreCase)
    foreach ($raw in $feature.productionPaths) {
        $relative = ([string]$raw).Replace('\','/')
        if (!$relative.StartsWith('src/',[StringComparison]::Ordinal) -or $relative -match '(^|/)\.\.?(/|$)|:' -or
            !$unique.Add($relative) -or !(Test-Path -LiteralPath (Join-Path $root $relative) -PathType Leaf)) {
            throw "Invalid or duplicate mapped source path: $relative"
        }
        if (!$mapped.ContainsKey($relative)) {
            $mapped.Add($relative,[pscustomobject]@{
                path=$relative; featureCount=0
                lines=(New-Object 'System.Collections.Generic.HashSet[int]')
                covered=(New-Object 'System.Collections.Generic.HashSet[int]')
            })
        }
        if ($mapped[$relative].path -cne $relative) { throw "Conflicting source path spelling: $relative" }
        $mapped[$relative].featureCount++
    }
}
function Get-RepositoryRelative([string]$Value) {
    $value = $Value.Replace('\','/')
    if (!$value -or $value -match '(^|/)\.\.?(/|$)|://') { return $null }
    if ($value.StartsWith($prefix,[StringComparison]::OrdinalIgnoreCase)) { return $value.Substring($prefix.Length) }
    # Deterministic .NET source paths have the explicit /_/ repository root.
    if ($value.StartsWith('/_/',[StringComparison]::Ordinal)) { return $value.Substring(3) }
    if ($value.StartsWith('/') -or $value -match ':') { return $null }
    return $value
}
function Resolve-MappedClass([string]$Filename,[string[]]$SourceRoots) {
    $normalized = $Filename.Replace('\','/')
    $candidates = New-Object 'System.Collections.Generic.HashSet[string]' -ArgumentList ([StringComparer]::OrdinalIgnoreCase)
    $direct = Get-RepositoryRelative $normalized
    if ($direct -and $mapped.ContainsKey($direct)) { [void]$candidates.Add($mapped[$direct].path) }
    if (!$normalized.StartsWith('/') -and $normalized -notmatch ':|(^|/)\.\.?(/|$)') {
        foreach ($source in $SourceRoots) {
            if ([string]::IsNullOrWhiteSpace($source)) { continue }
            $joined = $source.Replace('\','/').TrimEnd('/') + '/' + $normalized
            $relative = Get-RepositoryRelative $joined
            if ($relative -and $mapped.ContainsKey($relative)) { [void]$candidates.Add($mapped[$relative].path) }
        }
    }
    if ($candidates.Count -eq 1) {
        return [pscustomobject]@{path=(@($candidates)[0]);ambiguous=$false}
    }
    return [pscustomobject]@{path=$null;ambiguous=($candidates.Count -gt 1)}
}

$seenReports = New-Object 'System.Collections.Generic.HashSet[string]' -ArgumentList ([StringComparer]::OrdinalIgnoreCase)
$unmapped = 0
$ambiguous = 0
if ($ReportPaths.Count -eq 0) { throw 'No coverage reports supplied.' }
foreach ($report in $ReportPaths) {
    $full = (Resolve-Path -LiteralPath $report).Path
    if (!$seenReports.Add($full) -or !(Test-Path -LiteralPath $full -PathType Leaf)) { throw "Duplicate or invalid report: $full" }
    $settings = New-Object System.Xml.XmlReaderSettings
    $settings.DtdProcessing = [System.Xml.DtdProcessing]::Prohibit
    $settings.XmlResolver = $null
    $reader = [System.Xml.XmlReader]::Create($full,$settings)
    try {
        $document = New-Object System.Xml.XmlDocument
        $document.XmlResolver = $null
        $document.Load($reader)
    }
    finally { $reader.Dispose() }
    $classes = $document.SelectNodes('/coverage/packages/package/classes/class')
    if ($classes.Count -eq 0) { throw "Cobertura class evidence missing: $full" }
    $sources = @($document.SelectNodes('/coverage/sources/source') | ForEach-Object { $_.InnerText })
    foreach ($class in $classes) {
        $resolved = Resolve-MappedClass ($class.GetAttribute('filename')) $sources
        if ($resolved.ambiguous) { $ambiguous++; continue }
        if (!$resolved.path) { $unmapped++; continue }
        $row = $mapped[$resolved.path]
        foreach ($line in $class.SelectNodes('lines/line')) {
            $number = 0
            $hits = [long]0
            if (![int]::TryParse($line.GetAttribute('number'),[Globalization.NumberStyles]::None,
                    [Globalization.CultureInfo]::InvariantCulture,[ref]$number) -or $number -le 0 -or
                ![long]::TryParse($line.GetAttribute('hits'),[Globalization.NumberStyles]::None,
                    [Globalization.CultureInfo]::InvariantCulture,[ref]$hits)) {
                throw "Invalid Cobertura line evidence for $($row.path) in $full"
            }
            [void]$row.lines.Add($number)
            if ($hits -gt 0) { [void]$row.covered.Add($number) }
        }
    }
}
$features = New-Object 'System.Collections.Generic.List[object]'
foreach ($feature in $matrix.features) {
    $lines = 0
    $covered = 0
    $files = New-Object 'System.Collections.Generic.List[object]'
    $missing = New-Object 'System.Collections.Generic.List[string]'
    foreach ($path in $feature.productionPaths) {
        $row = $mapped[([string]$path).Replace('\','/')]
        $lines += $row.lines.Count
        $covered += $row.covered.Count
        if ($row.lines.Count -eq 0) { $missing.Add($row.path) }
        $files.Add([pscustomobject]@{path=$row.path;lines=$row.lines.Count;coveredLines=$row.covered.Count})
    }
    $features.Add([pscustomobject]@{
        id=$feature.id; risk=$feature.risk; lines=$lines; coveredLines=$covered
        linePercent=if ($lines -gt 0) { [math]::Round(100.0*$covered/$lines,2) } else { $null }
        completePathMeasurement=($missing.Count -eq 0)
        uninstrumentedPaths=$missing.ToArray()
        sourceFiles=$files.ToArray()
    })
}
$totalLines = 0
$totalCovered = 0
$measuredFiles = 0
$shared = New-Object 'System.Collections.Generic.List[string]'
foreach ($row in $mapped.Values) {
    $totalLines += $row.lines.Count
    $totalCovered += $row.covered.Count
    if ($row.lines.Count -gt 0) { $measuredFiles++ }
    if ($row.featureCount -gt 1) { $shared.Add($row.path) }
}
[pscustomobject]@{
    schemaVersion=1; sourceSha=$SourceSha.ToLowerInvariant(); suite=$Suite; reportCount=$seenReports.Count
    scope='Union of instrumented lines in mapped feature source files, not overall repository or branch coverage.'
    mappedFiles=$measuredFiles; registeredMappedFiles=$mapped.Count; mappedLines=$totalLines; coveredMappedLines=$totalCovered
    unmappedClassCount=$unmapped; ambiguousClassCount=$ambiguous
    sharedMappedPaths=@($shared | Sort-Object)
    features=$features.ToArray()
}
