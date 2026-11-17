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
release_artifact_sha256=aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa

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


    # The first pass supplied no trusted GitHub release. A green Windows build is
    # not sufficient evidence of a public, immutable, source-matching release.
    if([string]$revision.status -ne 'v9.9.9-source-verified-publication-unverified'){
        throw 'Sync regression: source verification falsely inherited a candidate/published status.'
    }

    # Restore the exact 0-failure input after the rejection fixture.
    Set-Content -LiteralPath $evidencePath -Value ($bad -replace 'source_sha=bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb',("source_sha="+$source)) -Encoding utf8

    $artifacts=Join-Path $fixture 'artifacts'
    New-Item -ItemType Directory -Force -Path $artifacts | Out-Null
    $zipHash='a'*64
    $productManifest=[ordered]@{
        productVersion='9.9.9'
        sourceSha=$source
        buildNumber=900
        artifactName='MHW-Manual-Mod-Manager-v9.9.9-win-x64.zip'
        artifactSize=8000
        sha256=$zipHash
    }
    $manifestPath=Join-Path $artifacts 'update-manifest.json'
    [IO.File]::WriteAllText($manifestPath,($productManifest | ConvertTo-Json -Depth 10),(New-Object Text.UTF8Encoding($false)))
    $manifestHash=(Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash.ToLowerInvariant()
    $manifestSize=(Get-Item -LiteralPath $manifestPath).Length

    $published=[ordered]@{
        tag_name='updater-main-900'
        target_commitish=$source
        immutable=$true
        draft=$false
        prerelease=$false
        assets=@(
            [ordered]@{name=$productManifest.artifactName;size=8000;digest=("sha256:"+$zipHash)}
            [ordered]@{name='update-manifest.json';size=$manifestSize;digest=("sha256:"+$manifestHash)}
        )
    }
    $tagRef=[ordered]@{
        ref='refs/tags/updater-main-900'
        object=[ordered]@{type='commit';sha=$source}
    }
    $releasePath=Join-Path $fixture 'published-release.json'
    $tagPath=Join-Path $fixture 'published-tag-ref.json'
    [IO.File]::WriteAllText($releasePath,($published | ConvertTo-Json -Depth 20),(New-Object Text.UTF8Encoding($false)))
    [IO.File]::WriteAllText($tagPath,($tagRef | ConvertTo-Json -Depth 10),(New-Object Text.UTF8Encoding($false)))
    $sync=Join-Path $fixture 'scripts\release\Sync-VerificationContinuity.ps1'
    & $sync -Root $fixture -SourceSha $source -RunId $run -EvidencePath $evidenceRelative -ManifestPath $manifestPath -ReleaseJsonPath $releasePath -TagRefJsonPath $tagPath

    $verified=Get-Content -LiteralPath (Join-Path $fixture '_AGENT_CONTEXT\CURRENT_REVISION.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    if([string]$verified.status -ne 'v9.9.9-immutable-release-published'){throw 'Sync regression: verified release was not marked as published.'}
    if([string]$verified.publishedRelease.tag -ne 'updater-main-900' -or
       [string]$verified.publishedRelease.sourceSha -ne $source -or
       [string]$verified.publishedRelease.artifactSha256 -ne $zipHash -or
       [string]$verified.publishedRelease.manifestSha256 -ne $manifestHash){
        throw 'Sync regression: current source-bound immutable publication receipt is incomplete.'
    }
    foreach($rel in @('_AGENT_CONTEXT\CURRENT_STATE.md','NEXT-AGENT-START-HERE.md')){
        $txt=Get-Content -LiteralPath (Join-Path $fixture $rel) -Raw -Encoding UTF8
        if($txt -notmatch 'updater-main-900' -or $txt -notmatch [regex]::Escape($source) -or $txt -match '(?im)^# v9\.9\.9 .*candidate'){
            throw "Sync regression: $rel kept stale candidate prose after publication."
        }
        if($txt -notmatch '## (?:Remaining independent work|Unresolved risks and next work)'){
            throw "Sync regression: $rel destroyed unrelated/historical work."
        }
    }

    $paths=@('_AGENT_CONTEXT\CURRENT_REVISION.json','_AGENT_CONTEXT\CURRENT_STATE.md','NEXT-AGENT-START-HERE.md')
    $beforeIdempotent=@{}
    foreach($rel in $paths){$beforeIdempotent[$rel]=[IO.File]::ReadAllBytes((Join-Path $fixture $rel))}
    & $sync -Root $fixture -SourceSha $source -RunId $run -EvidencePath $evidenceRelative -ManifestPath $manifestPath -ReleaseJsonPath $releasePath -TagRefJsonPath $tagPath
    foreach($rel in $paths){
        $current=[IO.File]::ReadAllBytes((Join-Path $fixture $rel))
        if(-not [Linq.Enumerable]::SequenceEqual([byte[]]$beforeIdempotent[$rel],[byte[]]$current)){
            throw "Sync regression: repeated verified publication changed $rel bytes."
        }
    }

    $negative=@(
        @{field='target_commitish';value=('b'*40)}
        @{field='immutable';value=$false}
        @{field='draft';value=$true}
        @{field='tag_name';value='updater-main-901'}
    )
    foreach($case in $negative){
        $tampered=$published | ConvertTo-Json -Depth 20 | ConvertFrom-Json
        $tampered.PSObject.Properties[$case.field].Value=$case.value
        [IO.File]::WriteAllText($releasePath,($tampered | ConvertTo-Json -Depth 20),(New-Object Text.UTF8Encoding($false)))
        $caught=$false
        try{& $sync -Root $fixture -SourceSha $source -RunId $run -EvidencePath $evidenceRelative -ManifestPath $manifestPath -ReleaseJsonPath $releasePath -TagRefJsonPath $tagPath *> $null}catch{$caught=$true}
        if(-not $caught){throw "Sync regression: tampered public release $($case.field) was accepted."}
        foreach($rel in $paths){
            $current=[IO.File]::ReadAllBytes((Join-Path $fixture $rel))
            if(-not [Linq.Enumerable]::SequenceEqual([byte[]]$beforeIdempotent[$rel],[byte[]]$current)){
                throw "Sync regression: rejected publication mutated $rel."
            }
        }
    }
    [IO.File]::WriteAllText($releasePath,($published | ConvertTo-Json -Depth 20),(New-Object Text.UTF8Encoding($false)))
    $wrongTag=$tagRef | ConvertTo-Json -Depth 10 | ConvertFrom-Json
    $wrongTag.object.sha='c'*40
    [IO.File]::WriteAllText($tagPath,($wrongTag | ConvertTo-Json -Depth 10),(New-Object Text.UTF8Encoding($false)))
    $tagRejected=$false
    try{& $sync -Root $fixture -SourceSha $source -RunId $run -EvidencePath $evidenceRelative -ManifestPath $manifestPath -ReleaseJsonPath $releasePath -TagRefJsonPath $tagPath *> $null}catch{$tagRejected=$true}
    if(-not $tagRejected){throw 'Sync regression: mismatched public tag ref SHA was accepted.'}

    [IO.File]::WriteAllText($tagPath,($tagRef | ConvertTo-Json -Depth 10),(New-Object Text.UTF8Encoding($false)))
    $wrongDigest=$published | ConvertTo-Json -Depth 20 | ConvertFrom-Json
    $wrongDigest.assets[0].digest='sha256:'+('f'*64)
    [IO.File]::WriteAllText($releasePath,($wrongDigest | ConvertTo-Json -Depth 20),(New-Object Text.UTF8Encoding($false)))
    $digestRejected=$false
    try{& $sync -Root $fixture -SourceSha $source -RunId $run -EvidencePath $evidenceRelative -ManifestPath $manifestPath -ReleaseJsonPath $releasePath -TagRefJsonPath $tagPath *> $null}catch{$digestRejected=$true}
    if(-not $digestRejected){throw 'Sync regression: wrong published ZIP digest was accepted.'}
    if(-not [Linq.Enumerable]::SequenceEqual([byte[]]$beforeIdempotent['_AGENT_CONTEXT\CURRENT_REVISION.json'],[byte[]][IO.File]::ReadAllBytes((Join-Path $fixture '_AGENT_CONTEXT\CURRENT_REVISION.json')))){
        throw 'Sync regression: invalid release digest modified continuity state.'
    }

    Write-Host 'PASS: verification continuity sync helper projects exact evidence and fails closed on mismatched source.' -ForegroundColor Green
}finally{
    if(Test-Path -LiteralPath $fixture){Remove-Item -LiteralPath $fixture -Recurse -Force -ErrorAction SilentlyContinue}
}
