[CmdletBinding()]
param(
    [ValidateRange(1,720)]
    [int]$MinimumAgeHours=24
)

$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest

$markerName='.mhw-test-scratch.json'
$tempRoot=[IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\')
$patterns=@(
    @{ Pattern='MhwManagerSelfTest-*'; Owner='MhwManagerSelfTest' },
    @{ Pattern='mhw-updater-installed-e2e-*'; Owner='UpdaterInstalledClientE2ETests' }
)
$now=[DateTime]::UtcNow
$removed=0
$skipped=0
$failed=0

function Test-SameProcess($Marker) {
    $pidValue=0
    if(-not [int]::TryParse([string]$Marker.processId,[ref]$pidValue) -or $pidValue -le 0){ return $false }
    $process=Get-Process -Id $pidValue -ErrorAction SilentlyContinue
    if($null -eq $process){ return $false }
    try {
        $expected=[DateTime]::Parse(
            [string]$Marker.processStartUtc,
            [Globalization.CultureInfo]::InvariantCulture,
            [Globalization.DateTimeStyles]::RoundtripKind).ToUniversalTime()
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
            $marker=Get-Content -LiteralPath $markerPath -Raw | ConvertFrom-Json
            if($marker.schema -cne 'mhw-test-scratch/v1' -or $marker.owner -cne $spec.Owner){
                $script:skipped++
                return
            }
            $created=[DateTime]::Parse(
                [string]$marker.createdUtc,
                [Globalization.CultureInfo]::InvariantCulture,
                [Globalization.DateTimeStyles]::RoundtripKind).ToUniversalTime()
            if(($now-$created).TotalHours -lt $MinimumAgeHours -or (Test-SameProcess $marker)){
                $script:skipped++
                return
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
