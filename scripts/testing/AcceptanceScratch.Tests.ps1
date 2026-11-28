$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Assert-That([bool]$Value, [string]$Message) {
    if (!$Value) { throw $Message }
}

$previousTemp = $env:TEMP
$previousTmp = $env:TMP
$parent = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
if ($parent -ne [IO.Path]::GetPathRoot($parent)) { $parent = $parent.TrimEnd('\', '/') }
$testRoot = Join-Path $parent ('mhw-scratch-contract-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testRoot | Out-Null
try {
    $env:TEMP = $testRoot
    $env:TMP = $testRoot
    . (Join-Path $PSScriptRoot 'AcceptanceScratch.ps1')

    $volumeRoot = [IO.Path]::GetPathRoot($testRoot)
    $env:TEMP = $volumeRoot
    $env:TMP = $volumeRoot
    Assert-That ((Get-MhwAcceptanceTempRoot) -ceq $volumeRoot) 'Temp volume root lost its separator.'
    $env:TEMP = $testRoot
    $env:TMP = $testRoot

    $scratch = New-MhwAcceptanceScratch
    $package = Join-Path $scratch.Path 'package'
    New-Item -ItemType Directory -Path $package | Out-Null
    [IO.File]::WriteAllText((Join-Path $package 'file'), 'generated fixture')
    Assert-That ((Split-Path -Parent $scratch.Path) -eq $testRoot) 'Scratch escaped the selected temp root.'
    try {
        Remove-MhwAcceptanceScratch @{ Path = $scratch.Path; Token = 'invalid' }
        throw 'Wrong token was accepted.'
    } catch {
        if ($_.Exception.Message -notmatch 'marker identity mismatch') { throw }
    }
    Assert-That (Test-Path -LiteralPath $package) 'Wrong token removed scratch.'
    Remove-MhwAcceptanceScratch $scratch
    Assert-That (!(Test-Path -LiteralPath $scratch.Path)) 'Normal cleanup left the package behind.'
    Remove-MhwAcceptanceScratch $scratch

    $buildRoot = Join-Path $testRoot 'artifacts'
    New-Item -ItemType Directory -Path $buildRoot | Out-Null
    $retained = Join-Path $buildRoot 'ordinary-build.dll'
    [IO.File]::WriteAllText($retained, 'existing output')
    $build = New-MhwAcceptanceScratch -ScratchRoot $buildRoot
    [IO.File]::WriteAllText((Join-Path $build.Path 'test-build.dll'), 'disposable build output')
    try {
        Remove-MhwAcceptanceScratch $build
        throw 'Default cleanup accepted scratch under a different root.'
    } catch {
        if ($_.Exception.Message -notmatch 'Unexpected acceptance scratch path') { throw }
    }
    Remove-MhwAcceptanceScratch $build -ScratchRoot $buildRoot
    Assert-That (!(Test-Path -LiteralPath $build.Path)) 'Build cleanup left disposable output behind.'
    Assert-That (Test-Path -LiteralPath $retained) 'Build cleanup removed ordinary output.'

    if ($env:OS -eq 'Windows_NT') {
        $bootTime = [DateTime]::UtcNow.AddHours(-72)
        function Get-CimInstance {
            [CmdletBinding()]
            param($ClassName, $Property)
            Assert-That ($ClassName -eq 'Win32_OperatingSystem' -and $Property -eq 'LastBootUpTime') 'Unexpected boot lookup.'
            [pscustomobject]@{ LastBootUpTime = $bootTime }
        }
        $fixtureRepo = Join-Path $testRoot 'build-repo'
        $fixtureScripts = Join-Path $fixtureRepo 'scripts\testing'
        $fixtureArtifacts = Join-Path $fixtureRepo 'artifacts'
        New-Item -ItemType Directory -Force -Path $fixtureScripts, $fixtureArtifacts | Out-Null
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Clear-StaleTestScratch.ps1'), (Join-Path $PSScriptRoot 'AcceptanceScratch.ps1') -Destination $fixtureScripts
        $prior = New-MhwAcceptanceScratch -ScratchRoot $fixtureArtifacts -StandaloneBuild
        $live = New-MhwAcceptanceScratch -ScratchRoot $fixtureArtifacts -StandaloneBuild
        $foreignBuild = New-MhwAcceptanceScratch -ScratchRoot $fixtureArtifacts -StandaloneBuild
        $existing = Join-Path $fixtureArtifacts 'existing-output.dll'
        [IO.File]::WriteAllText($existing, 'ordinary build')
        $unmarkedBuild = Join-Path $fixtureArtifacts ('mhw-acceptance-' + [Guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Path $unmarkedBuild | Out-Null
        foreach ($entry in @($prior, $live, $foreignBuild)) {
            $markerPath = Join-Path $entry.Path '.mhw-test-scratch.json'
            $marker = Get-Content -LiteralPath $markerPath -Raw | ConvertFrom-Json
            $recordedBoot = if ($entry.Path -eq $live.Path) { $bootTime } else { $bootTime.AddDays(-3) }
            $marker.standaloneBuildBootUtc = $recordedBoot.ToString('o')
            $marker.processStartUtc = $recordedBoot.AddHours(1).ToString('o')
            $marker.createdUtc = $recordedBoot.AddHours(2).ToString('o')
            if ($entry.Path -eq $foreignBuild.Path) { $marker.standaloneBuildHost = 'other-build-host' }
            $marker | ConvertTo-Json | Set-Content -LiteralPath $markerPath -Encoding UTF8
        }
        $heartbeat = Join-Path $live.Path 'worker-ready'
        $worker = Start-Job -ArgumentList $heartbeat -ScriptBlock {
            param($ReadyPath)
            [IO.File]::WriteAllText($ReadyPath, 'live')
            Start-Sleep -Seconds 30
        }
        try {
            $deadline = [DateTime]::UtcNow.AddSeconds(15)
            while (!(Test-Path -LiteralPath $heartbeat) -and [DateTime]::UtcNow -lt $deadline) {
                Start-Sleep -Milliseconds 100
            }
            Assert-That ((Test-Path -LiteralPath $heartbeat) -and $worker.State -eq 'Running') 'Build worker fixture did not start.'
            $collector = Join-Path $fixtureScripts 'Clear-StaleTestScratch.ps1'
            & $collector -MinimumAgeHours 24
            Assert-That (Test-Path -LiteralPath $prior.Path) 'Default collection crossed into the standalone build root.'
            & $collector -MinimumAgeHours 24 -IncludeStandaloneBuildScratch
            Assert-That (!(Test-Path -LiteralPath $prior.Path)) 'Abandoned prior-boot build was retained.'
            foreach ($path in @($heartbeat, $foreignBuild.Path, $existing, $unmarkedBuild)) {
                Assert-That (Test-Path -LiteralPath $path) 'Live, foreign, ordinary or unmarked build output was removed.'
            }
        } finally {
            Stop-Job -Job $worker
            Remove-Job -Job $worker
        }
    }

    $stale = New-MhwAcceptanceScratch -ReclaimAfterProducerExit
    $active = New-MhwAcceptanceScratch -ReclaimAfterProducerExit
    $foreign = New-MhwAcceptanceScratch -ReclaimAfterProducerExit
    $workerScope = New-MhwAcceptanceScratch
    $unmarked = Join-Path $testRoot ('mhw-acceptance-' + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $unmarked | Out-Null
    foreach ($item in @($stale, $active, $foreign, $workerScope)) {
        $markerPath = Join-Path $item.Path '.mhw-test-scratch.json'
        $marker = Get-Content -LiteralPath $markerPath -Raw | ConvertFrom-Json
        $marker.createdUtc = [DateTime]::UtcNow.AddHours(-48).ToString('o')
        if ($item.Path -ne $active.Path) { $marker.processStartUtc = [DateTime]::UtcNow.AddHours(-48).ToString('o') }
        if ($item.Path -eq $foreign.Path) { $marker.owner = 'OtherProducer' }
        $marker | ConvertTo-Json | Set-Content -LiteralPath $markerPath -Encoding UTF8
    }
    $linked = New-MhwAcceptanceScratch -ReclaimAfterProducerExit
    $markerPath = Join-Path $linked.Path '.mhw-test-scratch.json'
    $marker = Get-Content -LiteralPath $markerPath -Raw | ConvertFrom-Json
    $marker.createdUtc = [DateTime]::UtcNow.AddHours(-48).ToString('o')
    $marker.processStartUtc = $marker.createdUtc
    $marker | ConvertTo-Json | Set-Content -LiteralPath $markerPath -Encoding UTF8
    $junction = Join-Path $linked.Path 'foreign'
    if ($env:OS -eq 'Windows_NT') { New-Item -ItemType Junction -Path $junction -Target $unmarked | Out-Null }
    else { New-Item -ItemType SymbolicLink -Path $junction -Target $unmarked | Out-Null }
    try {
        & (Join-Path $PSScriptRoot 'Clear-StaleTestScratch.ps1') -MinimumAgeHours 24
        Assert-That (!(Test-Path -LiteralPath $stale.Path)) 'Stale owned scratch was retained.'
        foreach($path in @($active.Path, $unmarked, $foreign.Path, $linked.Path, $workerScope.Path)) {
            Assert-That (Test-Path -LiteralPath $path) 'Active, foreign, unmarked, linked or potentially worker-owned scratch was removed.'
        }
    } finally {
        (Get-Item -LiteralPath $junction -Force).Delete()
    }
    Write-Host 'PASS: acceptance scratch ownership, cleanup, stale recovery and reparse containment.'
} finally {
    $env:TEMP = $previousTemp
    $env:TMP = $previousTmp
    if ([IO.Path]::GetDirectoryName($testRoot) -eq $parent -and (Test-Path -LiteralPath $testRoot)) {
        Remove-Item -LiteralPath $testRoot -Recurse -Force
    }
}
