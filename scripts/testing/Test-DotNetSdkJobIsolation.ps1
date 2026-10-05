param([string]$Root='')
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest

if([string]::IsNullOrWhiteSpace($Root)){
    $Root=(Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
}

function Get-OwnedDotNetInstallRoot {
    param(
        [Parameter(Mandatory=$true)][string]$RunnerTemp,
        [Parameter(Mandatory=$true)][string]$RunId,
        [Parameter(Mandatory=$true)][string]$RunAttempt,
        [Parameter(Mandatory=$true)][string]$Job
    )

    foreach($pair in @(
        @{Name='RunnerTemp';Value=$RunnerTemp},
        @{Name='RunId';Value=$RunId},
        @{Name='RunAttempt';Value=$RunAttempt},
        @{Name='Job';Value=$Job}
    )){
        if([string]::IsNullOrWhiteSpace([string]$pair.Value)){
            throw "$($pair.Name) must not be empty."
        }
    }

    $jobToken=($Job -replace '[^A-Za-z0-9._-]','-')
    $candidate=Join-Path $RunnerTemp ("dotnet-{0}-{1}-{2}" -f $RunId,$RunAttempt,$jobToken)
    $runnerFull=[IO.Path]::GetFullPath($RunnerTemp).TrimEnd([char[]]@('\','/'))+[IO.Path]::DirectorySeparatorChar
    $candidateFull=[IO.Path]::GetFullPath($candidate)
    if(!$candidateFull.StartsWith($runnerFull,[StringComparison]::OrdinalIgnoreCase)){
        throw "Owned SDK root escaped runner temp: $candidateFull"
    }
    return $candidateFull
}

$scratch=Join-Path ([IO.Path]::GetTempPath()) ('mhw-sdk-job-isolation-'+[Guid]::NewGuid().ToString('N'))
$job=$null
try {
    New-Item -ItemType Directory -Force -Path $scratch | Out-Null

    $jobA=Get-OwnedDotNetInstallRoot -RunnerTemp $scratch -RunId '4242' -RunAttempt '1' -Job 'windows-release'
    $jobB=Get-OwnedDotNetInstallRoot -RunnerTemp $scratch -RunId '4242' -RunAttempt '1' -Job 'workflow-feature'
    $jobC=Get-OwnedDotNetInstallRoot -RunnerTemp $scratch -RunId '4242' -RunAttempt '2' -Job 'windows-release'
    $jobUnsafe=Get-OwnedDotNetInstallRoot -RunnerTemp $scratch -RunId '4242' -RunAttempt '1' -Job 'verify:release/unsafe'

    if([string]::Equals($jobA,$jobB,[StringComparison]::OrdinalIgnoreCase) -or
       [string]::Equals($jobA,$jobC,[StringComparison]::OrdinalIgnoreCase) -or
       [string]::Equals($jobB,$jobC,[StringComparison]::OrdinalIgnoreCase)){
        throw 'Run/attempt/job identities did not produce distinct SDK roots.'
    }
    if((Split-Path -Leaf $jobUnsafe) -match '[:\\/]'){
        throw "Sanitized job token still contains a path separator: $jobUnsafe"
    }

    foreach($path in @($jobA,$jobB)){
        New-Item -ItemType Directory -Force -Path $path | Out-Null
    }
    $markerA=Join-Path $jobA 'owner-a.txt'
    $markerB=Join-Path $jobB 'owner-b.txt'
    Set-Content -LiteralPath $markerA -Value 'A' -Encoding ASCII
    Set-Content -LiteralPath $markerB -Value 'B' -Encoding ASCII

    # This test's security invariant is sibling SDK-root ownership/isolation.
    # Keep the concurrent worker filesystem-only: external dotnet process startup
    # latency is unrelated to whether cleanup of one owned root can affect another.
    $job=Start-Job -ScriptBlock {
        param([string]$PeerMarker)
        $ErrorActionPreference='Stop'
        for($i=0;$i -lt 24;$i++){
            if(!(Test-Path -LiteralPath $PeerMarker -PathType Leaf)){
                throw "Peer-owned marker disappeared during concurrent cleanup: $PeerMarker"
            }
            if((Get-Content -LiteralPath $PeerMarker -Raw).Trim() -ne 'B'){
                throw "Peer-owned marker changed during concurrent sibling cleanup: $PeerMarker"
            }
            Start-Sleep -Milliseconds 20
        }
        'PASS'
    } -ArgumentList $markerB

    for($i=0;$i -lt 24;$i++){
        if(Test-Path -LiteralPath $jobA){
            Remove-Item -LiteralPath $jobA -Recurse -Force
        }
        New-Item -ItemType Directory -Force -Path $jobA | Out-Null
        Set-Content -LiteralPath $markerA -Value ('A-'+$i) -Encoding ASCII
        if(!(Test-Path -LiteralPath $markerB -PathType Leaf)){
            throw 'Cleaning job A modified job B.'
        }
        Start-Sleep -Milliseconds 10
    }

    $completed=Wait-Job -Job $job -Timeout 30
    if($null -eq $completed){
        Stop-Job -Job $job -ErrorAction SilentlyContinue
        throw 'Concurrent SDK-isolation probe timed out.'
    }
    $output=@(Receive-Job -Job $job -ErrorAction Stop)
    if($job.State -ne 'Completed' -or -not ($output -contains 'PASS')){
        $reason=$job.ChildJobs[0].JobStateInfo.Reason
        throw "Concurrent SDK-isolation worker failed: $reason"
    }

    if(!(Test-Path -LiteralPath $markerB -PathType Leaf)){
        throw 'Peer-owned SDK root was removed by sibling cleanup.'
    }
    if((Get-Content -LiteralPath $markerB -Raw).Trim() -ne 'B'){
        throw 'Peer-owned SDK root contents changed during sibling cleanup.'
    }

    Write-Host 'PASS: per-job .NET SDK roots remain isolated under concurrent sibling cleanup.' -ForegroundColor Green
}
finally {
    if($null -ne $job){
        Remove-Job -Job $job -Force -ErrorAction SilentlyContinue
    }
    if(Test-Path -LiteralPath $scratch){
        Remove-Item -LiteralPath $scratch -Recurse -Force -ErrorAction SilentlyContinue
    }
}
