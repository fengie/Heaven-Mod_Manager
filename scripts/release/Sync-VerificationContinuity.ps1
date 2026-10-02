param(
    [string]$Root,
    [Parameter(Mandatory=$true)][string]$SourceSha,
    [Parameter(Mandatory=$true)][string]$RunId,
    [Parameter(Mandatory=$true)][string]$EvidencePath
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

$revisionPath=Join-Path $Root '_AGENT_CONTEXT\CURRENT_REVISION.json'
$revision=(Read-Utf8 $revisionPath) | ConvertFrom-Json
if([string]$revision.currentVersion -ne $version){throw 'CURRENT_REVISION currentVersion must already match VERSION.txt before evidence synchronization.'}
$revision.verificationAppliesToCommit=$SourceSha
$revision.lastClosedVerificationCommit=$SourceSha
$revision.lastClosedVerificationRun="v$version source ${SourceSha}: hosted Windows verification run $RunId PASS; exact evidence $normalizedEvidence. The later evidence-only commit is not the tested source."
$revision.verificationScopeNote="v$version exact source $SourceSha passed hosted Windows verification run $RunId. Evidence-only persistence commits do not change tested-source identity; any later source/workflow/test change requires fresh exact-input verification."
$revision.nextMilestone="Current v$version hosted Windows verification is closed; continue the highest-priority actionable unowned item from _AGENT_CONTEXT/PROJECT_PLAN.md while preserving unresolved external security gates."
$revision.nextRequiredAction="Refresh canonical main and live ownership, then continue the next non-overlapping actionable project-plan item; do not reintegrate or rerun the already-verified v$version source solely because the evidence-only persistence commit advanced main."
$windows=[pscustomobject][ordered]@{sourceSha=$SourceSha;runId=$runNumber;evidence=$normalizedEvidence}
if($null -eq $revision.PSObject.Properties['lastClosedWindowsVerification']){
    Add-Member -InputObject $revision -MemberType NoteProperty -Name lastClosedWindowsVerification -Value $windows
}else{
    $revision.lastClosedWindowsVerification=$windows
}
Write-Utf8NoBom $revisionPath (($revision | ConvertTo-Json -Depth 20)+"`n")

$section=@"
## Verification boundary

Current hosted-Windows closure: v$version source ``$SourceSha`` passed run ``$RunId`` with 0 failed checks. Exact evidence: ``$normalizedEvidence``.

The tested source remains ``$SourceSha`` even though persistence creates a later evidence-only commit. Any source, workflow, test, or release-input change after that SHA requires fresh exact-input verification; an evidence-only commit must never be treated as the tested source.
"@
Replace-VerificationSection '_AGENT_CONTEXT/CURRENT_STATE.md' $section
Replace-VerificationSection 'NEXT-AGENT-START-HERE.md' $section

Write-Host "PASS: synchronized v$version hosted-Windows source $SourceSha run $RunId into canonical continuity state." -ForegroundColor Green
