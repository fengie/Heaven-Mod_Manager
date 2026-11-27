$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '..\ci\PublicWindowsEvidence.ps1')
$sha = 'a' * 40
$digest = 'c' * 64
$sentinel = 'PRIVATE_FIXTURE_ONLY_IN_DIAGNOSTICS'
function Assert-Rejected([scriptblock]$Call) {
    $rejected = $false
    try { & $Call | Out-Null } catch { $rejected = $true }
    if (-not $rejected) { throw 'Unsafe or inconsistent public receipt input was accepted.' }
}
function Assert-True([bool]$Condition) {
    if (-not $Condition) { throw 'Expected public receipt invariant was not met.' }
}
$report = "Overall: **PASS** - 26 passed / 0 failed" + [Environment]::NewLine + $sentinel
$projected = Get-MhwPublicWindowsVerification $report
Assert-True ($projected.status -ceq 'PASS' -and $projected.passed -eq 26 -and $projected.failed -eq 0)
Assert-Rejected { Get-MhwPublicWindowsVerification "Overall: **PASS** - 26 passed / 0 failed $sentinel" }
Assert-Rejected { Get-MhwPublicWindowsVerification "Overall: **PASS** - 0 passed / 0 failed" }
Assert-Rejected { Get-MhwPublicWindowsVerification "Overall: **PASS** - 26 passed / 9 failed" }
Assert-Rejected { Get-MhwPublicWindowsVerification ($report + [Environment]::NewLine + $report) }
Assert-Rejected { Get-MhwPublicCount '26' }
Assert-Rejected { Get-MhwPublicCount $true }
Assert-Rejected { Get-MhwPublicCount 1.2 }
Assert-Rejected { Get-MhwPublicCount -1 }
Assert-Rejected { Get-MhwPublicMeasure ([double]::NaN) }
Assert-Rejected { Get-MhwPublicSha ($sha + [Environment]::NewLine) }
Assert-Rejected { Get-MhwPublicSha @($sha,$sha) }

$manifest = [pscustomobject]@{
    sourceSha=$sha; sha256=$digest; productManifestSha256=('d' * 64)
    productVersion='8.8.146'; buildNumber=651; rawPath=("C:\Users\" + $sentinel)
}
Assert-True ((Get-MhwPublicReleasePackage $manifest $sha '8.8.146').zipSha256 -ceq $digest)
Assert-Rejected { Get-MhwPublicReleasePackage $manifest ('b' * 40) '8.8.146' }
Assert-Rejected { Get-MhwPublicReleasePackage $manifest $sha '8.8.147' }

$wpf = [pscustomobject]@{
    expectedCount=74; passedCount=74; failedCount=0; notRunCount=0; invalidCount=0; unexpectedCount=0
    complete=$true; exeSha256=$digest
    profiles=@(@{lastSuccess=$sentinel}); unexpectedReceipts=@($sentinel)
}
$projection = Get-MhwPublicWpfCounts $wpf
Assert-True ($projection.passedCount -eq 74 -and $projection.complete -and
    ($projection | ConvertTo-Json -Compress) -notmatch $sentinel)
$badWpf = $wpf.PSObject.Copy()
$badWpf.notRunCount=1
Assert-Rejected { Get-MhwPublicWpfCounts $badWpf }
$badWpf = $wpf.PSObject.Copy()
$badWpf.complete='true'
Assert-Rejected { Get-MhwPublicWpfCounts $badWpf }

$metric = [pscustomobject]@{process_to_ready_ms=50.1; peak_working_set_mb=16; rawPath=$sentinel}
$startup = [pscustomobject]@{
    source_sha=$sha; mod_count=3; files_per_mod=4; inbox_packages=1
    files_per_inbox_package=2; total_mod_files=12; package_size_mb=392
    cold=$metric; warm=$metric; runs=@($sentinel)
}
$projection = Get-MhwPublicStartupMeasurements $startup $sha
Assert-True ($projection.warm.process_to_ready_ms -eq 50.1 -and ($projection | ConvertTo-Json -Depth 6) -notmatch $sentinel)
Assert-Rejected { Get-MhwPublicStartupMeasurements $startup ('b' * 40) }
$metric.process_to_ready_ms=$sentinel
Assert-Rejected { Get-MhwPublicStartupMeasurements $startup $sha }

$oldSummary=$env:GITHUB_STEP_SUMMARY
$oldRun=$env:GITHUB_RUN_ID
$oldAttempt=$env:GITHUB_RUN_ATTEMPT
$oldEnvironment=$env:RUNNER_ENVIRONMENT
$oldOs=$env:RUNNER_OS
$oldTemp=$env:RUNNER_TEMP
$work = Join-Path ([IO.Path]::GetTempPath()) ('mhw-proof-test-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $work | Out-Null
$tmp = Join-Path $work 'summary.md'
try {
    $env:GITHUB_STEP_SUMMARY = $tmp
    $env:GITHUB_RUN_ID = '12345'
    $env:GITHUB_RUN_ATTEMPT = '1'
    $env:RUNNER_ENVIRONMENT = 'github-hosted'
    $env:RUNNER_OS = 'Windows'
    $identity = @{sourceSha=$sha; treeSha=('e' * 40); neverPublish=$sentinel}
    Write-MhwPublicSummary feature-pr $identity 'failure' @{ignored=$sentinel}
    Write-MhwPublicSummary packaged-wpf $identity 'success' $wpf
    Write-MhwPublicSummary windows-release $identity 'success' @{verificationText=$report; manifest=$manifest; version='8.8.146'}
    Assert-Rejected { Write-MhwPublicSummary updater-e2e $identity 'success' @{raw=$sentinel} }
    $env:RUNNER_ENVIRONMENT = 'self-hosted'
    Assert-Rejected { Write-MhwPublicSummary packaged-wpf $identity 'success' $wpf }
    $env:RUNNER_TEMP = $work
    $wrap = Join-Path $PSScriptRoot '..\ci\Invoke-PrivateWindowsCi.ps1'
    $nativeFixture = Join-Path $work 'child.ps1'
    @(
        'param([int]$ExitCode)'
        "Write-Output '$sentinel'"
        "[Console]::Error.WriteLine('$sentinel')"
        'exit $ExitCode'
    ) | Set-Content -LiteralPath $nativeFixture -Encoding UTF8
    $publicOutput = @(& $wrap -Stage public-fixture-ok -Program powershell.exe -ArgumentList @(
        '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $nativeFixture, '-ExitCode', '0') *>&1) -join [Environment]::NewLine
    Assert-True ($publicOutput -notmatch $sentinel -and $publicOutput -match 'passed')
    $wrapperFailed = $false
    try {
        & $wrap -Stage public-fixture-fail -Program powershell.exe -ArgumentList @(
            '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $nativeFixture, '-ExitCode', '7')
    } catch {
        $wrapperFailed = $_.Exception.Message -eq "MHW CI stage 'public-fixture-fail' failed. Raw diagnostics were not published."
    }
    Assert-True $wrapperFailed
    $captures = @(Get-ChildItem -LiteralPath $work -Filter diagnostics.log -Recurse -File)
    Assert-True ($captures.Count -eq 2)
    foreach ($capture in $captures) {
        Assert-True ((Get-Content -LiteralPath $capture.FullName -Raw) -match $sentinel)
    }
    $output = Get-Content -LiteralPath $tmp -Raw
    Assert-True ($output -notmatch $sentinel -and $output -match '"passedCount":\s+74' -and $output -match '"zipSha256"')
} finally {
    $env:GITHUB_STEP_SUMMARY = $oldSummary
    $env:GITHUB_RUN_ID = $oldRun
    $env:GITHUB_RUN_ATTEMPT = $oldAttempt
    $env:RUNNER_ENVIRONMENT = $oldEnvironment
    $env:RUNNER_OS = $oldOs
    $env:RUNNER_TEMP = $oldTemp
    if ($env:OS -eq 'Windows_NT') { $global:LASTEXITCODE = 0 }
    if (Test-Path -LiteralPath $work -PathType Container) { Remove-Item -LiteralPath $work -Recurse -Force }
}
Write-Host 'PASS: public Windows receipts reject malformed input and omit private fields.'
