$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Assert-That([bool]$Value, [string]$Message) {
    if (!$Value) { throw $Message }
}

$previousTemp = $env:TEMP
$previousTmp = $env:TMP
$parent = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
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
