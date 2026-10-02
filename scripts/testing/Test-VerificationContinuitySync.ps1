param([string]$Root)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
if([string]::IsNullOrWhiteSpace($Root)){$Root=(Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path}else{$Root=(Resolve-Path -LiteralPath $Root).Path}

$fixture=Join-Path ([IO.Path]::GetTempPath()) ('mhw-verification-continuity-'+[Guid]::NewGuid().ToString('N'))
$source='0123456789abcdef0123456789abcdef01234567'
$run='424242'
try{
    New-Item -ItemType Directory -Force -Path (Join-Path $fixture '_AGENT_CONTEXT\EVIDENCE') | Out-Null
    New-Item -ItemType Directory -Force -Path (Join-Path $fixture 'scripts\release') | Out-Null
    Copy-Item -LiteralPath (Join-Path $Root 'scripts\release\Sync-VerificationContinuity.ps1') -Destination (Join-Path $fixture 'scripts\release\Sync-VerificationContinuity.ps1')

    Set-Content -LiteralPath (Join-Path $fixture 'VERSION.txt') -Value '9.9.9' -Encoding utf8
    @'
{
  "formatVersion": 1,
  "currentVersion": "9.9.9",
  "verificationAppliesToCommit": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
  "lastClosedVerificationCommit": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
  "lastClosedVerificationRun": "predecessor",
  "verificationScopeNote": "predecessor",
  "nextMilestone": "predecessor",
  "nextRequiredAction": "predecessor"
}
'@ | Set-Content -LiteralPath (Join-Path $fixture '_AGENT_CONTEXT\CURRENT_REVISION.json') -Encoding utf8
    @'
# v9.9.9 test — canonical state

## Verification boundary

Predecessor evidence only.

## Remaining independent work

- test
'@ | Set-Content -LiteralPath (Join-Path $fixture '_AGENT_CONTEXT\CURRENT_STATE.md') -Encoding utf8
    @'
# v9.9.9 test — canonical handoff

## Verification boundary

Predecessor evidence only.

## Unresolved risks and next work

- test
'@ | Set-Content -LiteralPath (Join-Path $fixture 'NEXT-AGENT-START-HERE.md') -Encoding utf8

    $evidenceRelative='_AGENT_CONTEXT/EVIDENCE/v9.9.9-heaven-windows-closure.log'
    @"
MHW Manual Mod Manager v9.9.9 Heaven Windows closure
source_sha=$source
run_id=$run
runner_os=Windows

=== VERIFICATION REPORT ===
Overall: **PASS** - 26 passed / 0 failed
"@ | Set-Content -LiteralPath (Join-Path $fixture ($evidenceRelative.Replace([char]47,[char]92))) -Encoding utf8

    & (Join-Path $fixture 'scripts\release\Sync-VerificationContinuity.ps1') -Root $fixture -SourceSha $source -RunId $run -EvidencePath $evidenceRelative

    $revision=Get-Content -LiteralPath (Join-Path $fixture '_AGENT_CONTEXT\CURRENT_REVISION.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    if([string]$revision.verificationAppliesToCommit -ne $source){throw 'Sync regression: verificationAppliesToCommit was not advanced.'}
    if([string]$revision.lastClosedVerificationCommit -ne $source){throw 'Sync regression: lastClosedVerificationCommit was not advanced.'}
    if([long]$revision.lastClosedWindowsVerification.runId -ne [long]$run){throw 'Sync regression: run ID was not projected.'}
    foreach($relative in @('_AGENT_CONTEXT\CURRENT_STATE.md','NEXT-AGENT-START-HERE.md')){
        $text=Get-Content -LiteralPath (Join-Path $fixture $relative) -Raw -Encoding UTF8
        if(-not $text.Contains($source) -or -not $text.Contains($run)){throw "Sync regression: $relative does not carry exact source/run."}
        if($text -match '(?i)predecessor evidence only'){throw "Sync regression: $relative retained stale verification prose."}
    }

    $evidencePath=Join-Path $fixture ($evidenceRelative.Replace([char]47,[char]92))
    $bad=(Get-Content -LiteralPath $evidencePath -Raw -Encoding UTF8) -replace "source_sha=$source",'source_sha=bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb'
    Set-Content -LiteralPath $evidencePath -Value $bad -Encoding utf8
    $rejected=$false
    try{& (Join-Path $fixture 'scripts\release\Sync-VerificationContinuity.ps1') -Root $fixture -SourceSha $source -RunId $run -EvidencePath $evidenceRelative *> $null}catch{$rejected=$true}
    if(-not $rejected){throw 'Sync regression: mismatched evidence source was accepted.'}

    Write-Host 'PASS: verification continuity sync helper projects exact evidence and fails closed on mismatched source.' -ForegroundColor Green
}finally{
    if(Test-Path -LiteralPath $fixture){Remove-Item -LiteralPath $fixture -Recurse -Force -ErrorAction SilentlyContinue}
}
