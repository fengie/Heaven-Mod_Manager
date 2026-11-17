param(
    [string]$Root,
    [Parameter(Mandatory=$true)][string]$SourceSha,
    [Parameter(Mandatory=$true)][string]$RunId,
    [Parameter(Mandatory=$true)][string]$EvidencePath,
    [string]$ManifestPath,
    [string]$ReleaseJsonPath,
    [string]$TagRefJsonPath
)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest

if([string]::IsNullOrWhiteSpace($Root)){
    $Root=(Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
}else{
    $Root=(Resolve-Path -LiteralPath $Root).Path
}

function Read-Utf8([string]$Path){
    $bytes=[IO.File]::ReadAllBytes($Path)
    return (New-Object Text.UTF8Encoding($false,$true)).GetString($bytes).TrimStart([char]0xFEFF)
}
function Write-Utf8NoBom([string]$Path,[string]$Text){
    $tmp="$Path.tmp-$PID-$([Guid]::NewGuid().ToString('N'))"
    try{
        [IO.File]::WriteAllText($tmp,$Text,(New-Object Text.UTF8Encoding($false)))
        Move-Item -LiteralPath $tmp -Destination $Path -Force
    }finally{
        Remove-Item -LiteralPath $tmp -Force -ErrorAction SilentlyContinue
    }
}
function Set-CurrentReleasePreamble([string]$Relative,[string]$Title,[string]$CanonicalLine){
    $path=Join-Path $Root ($Relative.Replace([char]47,[char]92))
    $text=Read-Utf8 $path
    $firstSection=[regex]::Match($text,'(?m)^## ')
    if(-not $firstSection.Success){throw "$Relative is missing its first bounded section."}
    $preamble=$text.Substring(0,$firstSection.Index)
    $headline=[regex]::Match($preamble,"(?m)\A# v$([regex]::Escape($version))[^\r\n]*")
    if(-not $headline.Success){throw "$Relative is missing its current-version release heading."}

    # Collapse obsolete release identity lines in the preamble to one current
    # authoritative statement. Preserve other context and all ## sections.
    $tail=[regex]::Replace($preamble.Substring($headline.Length),
        '(?m)^Canonical (?:pre-release )?(?:source|state):[^\r\n]*(?:\r?\n)?','')
    $tail=$tail.TrimStart([char[]]@([char]13,[char]10))
    $updated=$Title+"`n`n"+$CanonicalLine+"`n`n"+$tail+$text.Substring($firstSection.Index)
    Write-Utf8NoBom $path $updated
}
function Set-CurrentReleaseNextSteps([string]$Section){
    $path=Join-Path $Root '_AGENT_CONTEXT\NEXT_STEPS.md'
    $text=Read-Utf8 $path
    $match=[regex]::Match($text,'(?ms)^## Current release acceptance[ \t]*\r?\n.*?(?=^## |\z)')
    if(-not $match.Success){
        throw 'NEXT_STEPS.md is missing the bounded Current release acceptance section.'
    }
    $replacement=$Section.TrimEnd()+"`n`n"
    Write-Utf8NoBom $path ($text.Substring(0,$match.Index)+$replacement+$text.Substring($match.Index+$match.Length))
}
function Test-SourceBoundPublication {
    param([string]$ReleasePath,[string]$TagPath,[string]$ArtifactManifestPath,[string]$Version,[string]$Source,[string]$Closure)
    foreach($p in @($ReleasePath,$TagPath,$ArtifactManifestPath)){
        if(-not (Test-Path -LiteralPath $p -PathType Leaf)){throw 'Verified publication input is missing.'}
    }
    $release=(Read-Utf8 $ReleasePath) | ConvertFrom-Json
    $tagRef=(Read-Utf8 $TagPath) | ConvertFrom-Json
    $manifest=(Read-Utf8 $ArtifactManifestPath) | ConvertFrom-Json
    $build=0L
    if([string]$manifest.productVersion -ne $Version -or [string]$manifest.sourceSha -cne $Source -or
       -not [long]::TryParse([string]$manifest.buildNumber,[ref]$build) -or $build -le 0){
        throw 'Publication manifest version/build/source does not match exact verification.'
    }
    $name="MHW-Manual-Mod-Manager-v$Version-win-x64.zip"
    $digest=([string]$manifest.sha256).ToLowerInvariant()
    $artifactSize=0L
    if([string]$manifest.artifactName -cne $name -or $digest -notmatch '^[0-9a-f]{64}$' -or
       -not [long]::TryParse([string]$manifest.artifactSize,[ref]$artifactSize) -or $artifactSize -le 0){
        throw 'Publication manifest artifact identity is invalid.'
    }
    $recorded=[regex]::Match($Closure,'(?im)^release_artifact_sha256=([0-9a-f]{64})\s*$')
    if(-not $recorded.Success -or -not [string]::Equals($recorded.Groups[1].Value,$digest,[StringComparison]::OrdinalIgnoreCase)){
        throw 'Publication digest disagrees with exact Windows verification closure.'
    }
    $tag="updater-main-$build"
    if([bool]$release.draft -or [bool]$release.prerelease -or -not [bool]$release.immutable -or
       [string]$release.tag_name -cne $tag -or [string]$release.target_commitish -cne $Source){
        throw 'Release API identity is not a public immutable exact-source release.'
    }
    if([string]$tagRef.ref -cne "refs/tags/$tag" -or
       [string]$tagRef.object.type -cne 'commit' -or [string]$tagRef.object.sha -cne $Source){
        throw 'Public tag ref does not point to exact source.'
    }
    $assets=@($release.assets)
    if($assets.Count -ne 2){throw 'Public release must have exactly the ZIP and manifest.'}
    $zip=@($assets | Where-Object { [string]$_.name -ceq $name })
    $sidecar=@($assets | Where-Object { [string]$_.name -ceq 'update-manifest.json' })
    if($zip.Count -ne 1 -or $sidecar.Count -ne 1){throw 'Public release asset identity ambiguous.'}
    if([string]$zip[0].digest -cne "sha256:$digest" -or [long]$zip[0].size -ne $artifactSize){
        throw 'Published ZIP digest/size differs from verified manifest.'
    }
    $sideHash=(Get-FileHash -LiteralPath $ArtifactManifestPath -Algorithm SHA256).Hash.ToLowerInvariant()
    $sideSize=(Get-Item -LiteralPath $ArtifactManifestPath).Length
    if([string]$sidecar[0].digest -cne "sha256:$sideHash" -or [long]$sidecar[0].size -ne $sideSize){
        throw 'Published manifest digest/size differs from built manifest bytes.'
    }
    return [pscustomobject][ordered]@{tag=$tag;version=$Version;sourceSha=$Source;buildNumber=$build;artifactName=$name;artifactSha256=$digest;manifestSha256=$sideHash}
}

function Replace-VerificationSection([string]$Relative,[string]$Section){
    $path=Join-Path $Root ($Relative.Replace([char]47,[char]92))
    $text=Read-Utf8 $path
    $match=[regex]::Match($text,'(?ms)^## Verification boundary\s*\r?\n.*?(?=^## |\z)')
    if(-not $match.Success){throw "$Relative is missing a bounded Verification boundary section."}
    $replacement=$Section.TrimEnd()+"`r`n`r`n"
    $updated=$text.Substring(0,$match.Index)+$replacement+$text.Substring($match.Index+$match.Length)
    Write-Utf8NoBom $path $updated
}

$version=(Read-Utf8 (Join-Path $Root 'VERSION.txt')).Trim()
if($SourceSha -notmatch '^[0-9a-fA-F]{40}$'){throw 'SourceSha must be an exact 40-character Git SHA.'}
$SourceSha=$SourceSha.ToLowerInvariant()
$runNumber=0L
if(-not [long]::TryParse($RunId,[ref]$runNumber) -or $runNumber -le 0){throw 'RunId must be a positive integer.'}
$RunId=[string]$runNumber

$normalizedEvidence=$EvidencePath.Replace('\','/')
$expectedEvidence="_AGENT_CONTEXT/EVIDENCE/v$version-heaven-windows-closure.log"
if(-not [string]::Equals($normalizedEvidence,$expectedEvidence,[StringComparison]::Ordinal)){
    throw "EvidencePath must be the current-version closure '$expectedEvidence'."
}
$evidenceFull=Join-Path $Root ($normalizedEvidence.Replace([char]47,[char]92))
if(-not (Test-Path -LiteralPath $evidenceFull -PathType Leaf)){throw "Closure evidence is missing: $normalizedEvidence"}
$evidence=Read-Utf8 $evidenceFull
if($evidence -notmatch "(?m)^MHW Manual Mod Manager v$([regex]::Escape($version)) Heaven Windows closure\s*$"){
    throw "Closure evidence version does not match VERSION.txt ($version)."
}
$sourceMatch=[regex]::Match($evidence,'(?im)^source_sha=([0-9a-f]{40})\s*$')
$runMatch=[regex]::Match($evidence,'(?im)^run_id=([0-9]+)\s*$')
if(-not $sourceMatch.Success -or -not $runMatch.Success){throw 'Closure evidence is missing source_sha or run_id.'}
if(-not [string]::Equals($sourceMatch.Groups[1].Value,$SourceSha,[StringComparison]::OrdinalIgnoreCase)){
    throw "Closure source_sha does not match requested exact source $SourceSha."
}
if([string]$runMatch.Groups[1].Value -ne $RunId){throw "Closure run_id does not match requested run $RunId."}
if($evidence -notmatch '(?im)^Overall:\s+\*\*PASS\*\*.*\b0 failed\b'){
    throw 'Closure evidence does not contain a successful 0-failure verification report.'
}

$publication=$null
$receiptInputs=@(@($ManifestPath,$ReleaseJsonPath,$TagRefJsonPath) | Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_) })
if($receiptInputs.Count -ne 0 -and $receiptInputs.Count -ne 3){
    throw 'Publication receipt requires manifest, public release and tag ref together.'
}
if($receiptInputs.Count -eq 3){
    $expectedManifest=Join-Path $Root 'artifacts\update-manifest.json'
    $givenManifest=[IO.Path]::GetFullPath($ManifestPath)
    if(-not [string]::Equals($givenManifest,[IO.Path]::GetFullPath($expectedManifest),[StringComparison]::OrdinalIgnoreCase)){
        throw 'Publication manifest must be the current build-owned artifact.'
    }
    $publication=Test-SourceBoundPublication -ReleasePath $ReleaseJsonPath -TagPath $TagRefJsonPath -ArtifactManifestPath $givenManifest -Version $version -Source $SourceSha -Closure $evidence
}

# Preflight all generated continuity surfaces before changing any of them.
$nextStepsText=Read-Utf8 (Join-Path $Root '_AGENT_CONTEXT\NEXT_STEPS.md')
if(-not [regex]::IsMatch($nextStepsText,'(?m)^## Current release acceptance[ \t]*\r?
$revision=(Read-Utf8 $revisionPath) | ConvertFrom-Json
if([string]$revision.currentVersion -ne $version){throw 'CURRENT_REVISION currentVersion must already match VERSION.txt before evidence synchronization.'}
$revision.verificationAppliesToCommit=$SourceSha
$revision.lastClosedVerificationCommit=$SourceSha
$revision.lastClosedVerificationRun="v$version source ${SourceSha}: hosted Windows verification run $RunId PASS; exact evidence $normalizedEvidence. The later evidence-only commit is not the tested source."
$revision.verificationScopeNote="v$version exact source $SourceSha passed hosted Windows verification run $RunId. Evidence-only persistence commits do not change tested-source identity; any later source/workflow/test change requires fresh exact-input verification."
if($null -ne $publication){
    $revision.status="v$version-immutable-release-published"
    $revision.nextMilestone="Current v$version immutable $($publication.tag) is source-verified at $SourceSha (Windows Gate $RunId), with exact ZIP/manifest SHA-256. Do not republish."
    $revision.nextRequiredAction="Continue unresolved real-device, corpus and independent trust acceptance in _AGENT_CONTEXT/PROJECT_PLAN.md. Preserve immutable release and its exact-source receipt."
    if($null -eq $revision.PSObject.Properties['publishedRelease']){
        Add-Member -InputObject $revision -MemberType NoteProperty -Name publishedRelease -Value $publication
    }else{$revision.publishedRelease=$publication}
}else{
    $revision.status="v$version-source-verified-publication-unverified"
    $revision.nextMilestone="Source v$version at $SourceSha passed Windows Gate $RunId, but exact public immutable publication for this source is NOT VERIFIED."
    $revision.nextRequiredAction="Verify a source-bound immutable public GitHub release and tag before claiming shipment. Another source's same-version release is not sufficient."
    if($null -ne $revision.PSObject.Properties['publishedRelease']){$revision.PSObject.Properties.Remove('publishedRelease')}
}
$windows=[pscustomobject][ordered]@{sourceSha=$SourceSha;runId=$runNumber;evidence=$normalizedEvidence}
if($null -eq $revision.PSObject.Properties['lastClosedWindowsVerification']){
    Add-Member -InputObject $revision -MemberType NoteProperty -Name lastClosedWindowsVerification -Value $windows
}else{
    $revision.lastClosedWindowsVerification=$windows
}
$publicationNote=if($null -ne $publication){
    "Verified immutable GitHub release: [$($publication.tag)](https://github.com/fengie/Heaven-Mod_Manager/releases/tag/$($publication.tag)), exact source ``$SourceSha``, ZIP SHA-256 ``$($publication.artifactSha256)`` and manifest SHA-256 ``$($publication.manifestSha256)``. Public release and tag ref independently match the built artifact."
}else{
    "Public release status: **NOT VERIFIED** for source ``$SourceSha``. Successful build or another source's same-version release is not proof of publication."
}
$headTitle=if($null -ne $publication){"# v$version - immutable release verified"}else{"# v$version - source verified, publication unverified"}
$intro=if($null -ne $publication){
    "Canonical source: ``fengie/Heaven-Mod_Manager@main`` at ``$SourceSha``. Public immutable [$($publication.tag)](https://github.com/fengie/Heaven-Mod_Manager/releases/tag/$($publication.tag)) contains this exact source. Bootstrap ``fengie/heaven-toolbox@main`` before other work."
}else{
    "Canonical source: ``fengie/Heaven-Mod_Manager@main`` verified at ``$SourceSha`` (Windows Gate $RunId); exact immutable release not yet verified. Bootstrap ``fengie/heaven-toolbox@main`` before other work."
}
$stateIntro=$intro.Replace('Canonical source:','Canonical state:')
Write-Utf8NoBom $revisionPath (($revision | ConvertTo-Json -Depth 20)+"`n")

$section=@"
## Verification boundary

Current hosted-Windows closure: v$version source ``$SourceSha`` passed run ``$RunId`` with 0 failed checks. Exact evidence: ``$normalizedEvidence``.

$publicationNote

The tested source remains ``$SourceSha`` even though persistence creates a later evidence-only commit. Any source, workflow, test, or release-input change after that SHA requires fresh exact-input verification; an evidence-only commit must never be treated as the tested source.
"@
Replace-VerificationSection '_AGENT_CONTEXT/CURRENT_STATE.md' $section
Replace-VerificationSection 'NEXT-AGENT-START-HERE.md' $section
$nextStepsSection=@"
## Current release acceptance

Current v$version Windows verification: source ``$SourceSha``; run ``$RunId`` (0 failures).

$publicationNote

This receipt does not establish that the build is installed on Heaven2. Continue real archive corpus, installed UI latency, recovery and independent publisher-trust work from _AGENT_CONTEXT/PROJECT_PLAN.md.
"@
Set-CurrentReleaseNextSteps $nextStepsSection
Set-CurrentReleasePreamble '_AGENT_CONTEXT/CURRENT_STATE.md' $headTitle $stateIntro
Set-CurrentReleasePreamble 'NEXT-AGENT-START-HERE.md' $headTitle $intro

Write-Host "PASS: synchronized v$version hosted-Windows source $SourceSha run $RunId into canonical continuity state." -ForegroundColor Green
)){
    throw 'NEXT_STEPS.md is missing the bounded Current release acceptance section.'
}
$revisionPath=Join-Path $Root '_AGENT_CONTEXT\CURRENT_REVISION.json'
$revision=(Read-Utf8 $revisionPath) | ConvertFrom-Json
if([string]$revision.currentVersion -ne $version){throw 'CURRENT_REVISION currentVersion must already match VERSION.txt before evidence synchronization.'}
$revision.verificationAppliesToCommit=$SourceSha
$revision.lastClosedVerificationCommit=$SourceSha
$revision.lastClosedVerificationRun="v$version source ${SourceSha}: hosted Windows verification run $RunId PASS; exact evidence $normalizedEvidence. The later evidence-only commit is not the tested source."
$revision.verificationScopeNote="v$version exact source $SourceSha passed hosted Windows verification run $RunId. Evidence-only persistence commits do not change tested-source identity; any later source/workflow/test change requires fresh exact-input verification."
if($null -ne $publication){
    $revision.status="v$version-immutable-release-published"
    $revision.nextMilestone="Current v$version immutable $($publication.tag) is source-verified at $SourceSha (Windows Gate $RunId), with exact ZIP/manifest SHA-256. Do not republish."
    $revision.nextRequiredAction="Continue unresolved real-device, corpus and independent trust acceptance in _AGENT_CONTEXT/PROJECT_PLAN.md. Preserve immutable release and its exact-source receipt."
    if($null -eq $revision.PSObject.Properties['publishedRelease']){
        Add-Member -InputObject $revision -MemberType NoteProperty -Name publishedRelease -Value $publication
    }else{$revision.publishedRelease=$publication}
}else{
    $revision.status="v$version-source-verified-publication-unverified"
    $revision.nextMilestone="Source v$version at $SourceSha passed Windows Gate $RunId, but exact public immutable publication for this source is NOT VERIFIED."
    $revision.nextRequiredAction="Verify a source-bound immutable public GitHub release and tag before claiming shipment. Another source's same-version release is not sufficient."
    if($null -ne $revision.PSObject.Properties['publishedRelease']){$revision.PSObject.Properties.Remove('publishedRelease')}
}
$windows=[pscustomobject][ordered]@{sourceSha=$SourceSha;runId=$runNumber;evidence=$normalizedEvidence}
if($null -eq $revision.PSObject.Properties['lastClosedWindowsVerification']){
    Add-Member -InputObject $revision -MemberType NoteProperty -Name lastClosedWindowsVerification -Value $windows
}else{
    $revision.lastClosedWindowsVerification=$windows
}
$publicationNote=if($null -ne $publication){
    "Verified immutable GitHub release: [$($publication.tag)](https://github.com/fengie/Heaven-Mod_Manager/releases/tag/$($publication.tag)), exact source ``$SourceSha``, ZIP SHA-256 ``$($publication.artifactSha256)`` and manifest SHA-256 ``$($publication.manifestSha256)``. Public release and tag ref independently match the built artifact."
}else{
    "Public release status: **NOT VERIFIED** for source ``$SourceSha``. Successful build or another source's same-version release is not proof of publication."
}
$headTitle=if($null -ne $publication){"# v$version - immutable release verified"}else{"# v$version - source verified, publication unverified"}
$intro=if($null -ne $publication){
    "Canonical source: ``fengie/Heaven-Mod_Manager@main`` at ``$SourceSha``. Public immutable [$($publication.tag)](https://github.com/fengie/Heaven-Mod_Manager/releases/tag/$($publication.tag)) contains this exact source. Bootstrap ``fengie/heaven-toolbox@main`` before other work."
}else{
    "Canonical source: ``fengie/Heaven-Mod_Manager@main`` verified at ``$SourceSha`` (Windows Gate $RunId); exact immutable release not yet verified. Bootstrap ``fengie/heaven-toolbox@main`` before other work."
}
$stateIntro=$intro.Replace('Canonical source:','Canonical state:')
Write-Utf8NoBom $revisionPath (($revision | ConvertTo-Json -Depth 20)+"`n")

$section=@"
## Verification boundary

Current hosted-Windows closure: v$version source ``$SourceSha`` passed run ``$RunId`` with 0 failed checks. Exact evidence: ``$normalizedEvidence``.

$publicationNote

The tested source remains ``$SourceSha`` even though persistence creates a later evidence-only commit. Any source, workflow, test, or release-input change after that SHA requires fresh exact-input verification; an evidence-only commit must never be treated as the tested source.
"@
Replace-VerificationSection '_AGENT_CONTEXT/CURRENT_STATE.md' $section
Replace-VerificationSection 'NEXT-AGENT-START-HERE.md' $section
$nextStepsSection=@"
## Current release acceptance

Current v$version Windows verification: source ``$SourceSha``; run ``$RunId`` (0 failures).

$publicationNote

This receipt does not establish that the build is installed on Heaven2. Continue real archive corpus, installed UI latency, recovery and independent publisher-trust work from _AGENT_CONTEXT/PROJECT_PLAN.md.
"@
Set-CurrentReleaseNextSteps $nextStepsSection
Set-CurrentReleasePreamble '_AGENT_CONTEXT/CURRENT_STATE.md' $headTitle $stateIntro
Set-CurrentReleasePreamble 'NEXT-AGENT-START-HERE.md' $headTitle $intro

Write-Host "PASS: synchronized v$version hosted-Windows source $SourceSha run $RunId into canonical continuity state." -ForegroundColor Green
