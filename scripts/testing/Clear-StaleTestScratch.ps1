[CmdletBinding()]
param(
    [ValidateRange(1,720)]
    [int]$MinimumAgeHours=24
)

$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest

. (Join-Path $PSScriptRoot 'AcceptanceScratch.ps1')
$markerName='.mhw-test-scratch.json'
$tempRoot=Get-MhwAcceptanceTempRoot
$patterns=@(
    @{ Pattern='MhwManagerSelfTest-*'; Owner='MhwManagerSelfTest' },
    @{ Pattern='mhw-updater-installed-e2e-*'; Owner='UpdaterInstalledClientE2ETests' }
    @{ Pattern='mhw-acceptance-*'; Owner='MhwAcceptanceScratch' }
)
$now=[DateTime]::UtcNow
$removed=0
$skipped=0
$failed=0

function Convert-MarkerTime($Value) {
    if($Value -is [DateTime]) { return $Value.ToUniversalTime() }
    return [DateTimeOffset]::Parse([string]$Value, [Globalization.CultureInfo]::InvariantCulture).UtcDateTime
}

function Test-SameProcess($Marker) {
    $pidValue=0
    if(-not [int]::TryParse([string]$Marker.processId,[ref]$pidValue) -or $pidValue -le 0){ return $true }
    $process=Get-Process -Id $pidValue -ErrorAction SilentlyContinue
    if($null -eq $process){ return $false }
    try {
        $expected=Convert-MarkerTime $Marker.processStartUtc
        $actual=$process.StartTime.ToUniversalTime()
        return [Math]::Abs(($actual-$expected).TotalSeconds) -lt 2
    } catch {
        return $true
    } finally {
        $process.Dispose()
    }
}

function Remove-WithRetry([string]$Path) {
    $last=$null
    for($attempt=1;$attempt -le 8;$attempt++){
        if(-not (Test-Path -LiteralPath $Path)){ return }
        try {
            Remove-Item -LiteralPath $Path -Recurse -Force -ErrorAction Stop
            return
        } catch {
            $last=$_.Exception
            if($attempt -lt 8){ Start-Sleep -Milliseconds (100*$attempt) }
        }
    }
    throw [IO.IOException]::new(('Failed to remove stale test scratch root after retries: '+$Path),$last)
}

foreach($spec in $patterns){
    Get-ChildItem -LiteralPath $tempRoot -Directory -Filter $spec.Pattern -Force -ErrorAction SilentlyContinue | ForEach-Object {
        $root=$_.FullName
        $markerPath=Join-Path $root $markerName
        if(($_.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 -or -not (Test-Path -LiteralPath $markerPath -PathType Leaf)){
            $script:skipped++
            return
        }

        try {
            if((Get-Item -LiteralPath $markerPath -Force).Attributes -band [IO.FileAttributes]::ReparsePoint){
                $script:skipped++
                return
            }
            $marker=Get-Content -LiteralPath $markerPath -Raw | ConvertFrom-Json
            if($marker.schema -cne 'mhw-test-scratch/v1' -or $marker.owner -cne $spec.Owner){
                $script:skipped++
                return
            }
            $created=Convert-MarkerTime $marker.createdUtc
            if(($now-$created).TotalHours -lt $MinimumAgeHours -or (Test-SameProcess $marker)){
                $script:skipped++
                return
            }

            if($spec.Owner -ceq 'MhwAcceptanceScratch'){
                # A launcher PID cannot establish the liveness of detached workers.
                $reclaim=$marker.PSObject.Properties['reclaimAfterProducerExit']
                if($null -eq $reclaim -or $reclaim.Value -isnot [bool] -or !$reclaim.Value){
                    $script:skipped++
                    return
                }
                if($marker.token -cnotmatch '^[a-f0-9]{32}$' -or $_.Name -cne ('mhw-acceptance-'+$marker.token) -or
                    [string]$marker.processId -notmatch '^[1-9][0-9]*$'){
                    $script:skipped++
                    return
                }
                Assert-MhwAcceptanceScratchTree $root
            }
            Remove-WithRetry $root
            $script:removed++
            Write-Host ('Removed stale marked test scratch: '+$root)
        } catch {
            $script:failed++
            Write-Warning ('Could not remove stale marked test scratch '+$root+': '+$_.Exception.Message)
        }
    }
}

Write-Host ("Test scratch maintenance: removed={0} skipped={1} failed={2}" -f $removed,$skipped,$failed)
