param([string]$Root)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
if([string]::IsNullOrWhiteSpace($Root)){$Root=(Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path}else{$Root=(Resolve-Path -LiteralPath $Root).Path}

function SourceBytes([string]$Relative){
    $p=Join-Path $Root ($Relative.Replace([char]47,[char]92))
    if(-not (Test-Path -LiteralPath $p)){throw "Missing required file: $Relative"}
    return ,([IO.File]::ReadAllBytes($p))
}
function Active([string]$Relative){
    $bytes=SourceBytes $Relative
    $utf8=New-Object Text.UTF8Encoding($false,$true)
    $text=$utf8.GetString($bytes).TrimStart([char]0xFEFF)
    return [regex]::Replace($text,'(?s)<!--.*?-->',' ')
}
function Need([string]$Text,[string]$Pattern,[string]$Message){if($Text -notmatch $Pattern){throw $Message}}
function Forbid([string]$Text,[string]$Pattern,[string]$Message){if($Text -match $Pattern){throw $Message}}
function MaxBytes([string]$Relative,[int]$Limit){
    $bytes=(SourceBytes $Relative).Length
    if($bytes -gt $Limit){throw "$Relative is $bytes UTF-8 bytes; limit is $Limit."}
    return $bytes
}
function Continuity([string]$Text,[string]$Name){
    Need $Text '(?i)\b(?:successor|next agent)\b[^\r\n]{0,180}\b(?:must|shall|is required to)\b[^\r\n]{0,260}\b(?:propagate|pass)\b[^\r\n]{0,180}\b(?:agent after|successor|next agent)\b' "$Name must require the successor to propagate continuity onward."
    Forbid $Text '(?is)(?:\bsuccessor\b|\bagent after\b).{0,100}(?:must|should|may|can)\s+not\b.{0,120}(?:inherit|preserve|propagate|obey)' "$Name must not negate successor continuity."
}

foreach($forbidden in @('_AGENT_TRAINING','plugins','heaven-bridge','tools','GLOBAL_GIT_DIRECTIVE.md')){
    if(Test-Path -LiteralPath (Join-Path $Root $forbidden)){throw "MHW must not contain migrated global ownership path: $forbidden"}
}

$manifest=Active '_AGENT_CONTEXT/handoff-manifest.json' | ConvertFrom-Json
if($manifest.formatVersion -ne 1 -or $manifest.continuityRequired -ne $true -or $manifest.propagateToNextAgent -ne $true){throw 'Invalid handoff manifest continuity contract.'}
if([string]$manifest.canonicalRepository -ne 'fengie/mhw-mods' -or [string]$manifest.canonicalBranch -ne 'main'){throw 'Unexpected canonical MHW repository/branch.'}
if([string]$manifest.globalBootstrapRepository -ne 'fengie/heaven-toolbox' -or [string]$manifest.globalBootstrapBranch -ne 'main'){throw 'Global bootstrap must route to fengie/heaven-toolbox@main.'}
if([string]$manifest.companyTrainer -ne 'fengie/heaven-toolbox@main:_AGENT_TRAINING/README.md'){throw 'companyTrainer must route to the canonical Toolbox trainer.'}
if([string]$manifest.globalGitDirective -ne 'fengie/heaven-toolbox@main:GLOBAL_GIT_DIRECTIVE.md'){throw 'globalGitDirective must route to the canonical Toolbox Git policy.'}
foreach($pointer in @{agentInstructions='AGENTS.md';currentRevisionFile='_AGENT_CONTEXT/CURRENT_REVISION.json';continuityProtocol='_AGENT_CONTEXT/CONTINUITY_PROTOCOL.md';learnedRules='_AGENT_CONTEXT/LEARNED_RULES.md';projectPlan='_AGENT_CONTEXT/PROJECT_PLAN.md'}.GetEnumerator()){
    if([string]$manifest.($pointer.Key) -ne $pointer.Value){throw "Invalid handoff manifest pointer: $($pointer.Key)."}
}

$version=(Active 'VERSION.txt').Trim()
[xml]$props=Active 'Directory.Build.props'
if([string]$props.Project.PropertyGroup.Version -ne $version){throw 'Directory.Build.props version must match VERSION.txt.'}
if([string]$manifest.currentVersion -ne $version){throw 'handoff-manifest currentVersion must match VERSION.txt.'}
$revision=Active '_AGENT_CONTEXT/CURRENT_REVISION.json' | ConvertFrom-Json
if($revision.formatVersion -ne 1 -or $revision.canonicalRepository -ne $manifest.canonicalRepository -or $revision.canonicalBranch -ne $manifest.canonicalBranch){throw 'Invalid current revision canonical identity.'}
if([string]$revision.currentVersion -ne $version){throw 'CURRENT_REVISION currentVersion must match VERSION.txt.'}
if([string]$revision.globalBootstrapRepository -ne 'fengie/heaven-toolbox' -or [string]$revision.globalBootstrapBranch -ne 'main'){throw 'CURRENT_REVISION must route global bootstrap to Heaven Toolbox main.'}
if([string]::IsNullOrWhiteSpace([string]$revision.status)){throw 'CURRENT_REVISION must contain a non-empty status.'}
if([string]$revision.integrationState -ne 'canonical-main'){throw 'CURRENT_REVISION integrationState must describe canonical-main state.'}
if([string]$revision.workingBranch -ne [string]$revision.canonicalBranch){throw 'CURRENT_REVISION workingBranch must equal canonicalBranch; feature-branch execution state must not ship as canonical handoff state.'}
if([string]$revision.status -match '(?i)\bcandidate\b'){throw 'CURRENT_REVISION status must describe canonical state, not a pre-merge candidate.'}
if($null -ne $revision.candidateSourceCommit -and -not [string]::IsNullOrWhiteSpace([string]$revision.candidateSourceCommit)){throw 'CURRENT_REVISION candidateSourceCommit must be null in canonical-main state.'}
if([string]::IsNullOrWhiteSpace([string]$revision.verificationAppliesToCommit)){throw 'CURRENT_REVISION must identify verificationAppliesToCommit.'}

$escaped=[regex]::Escape($version)
$readme=Active 'README.md'; $changelog=Active 'CHANGELOG.md'
Need $readme "(?m)^#\s+v$escaped\b" 'README title must show current version.'
Need $readme "(?m)^##\s+v$escaped\b" 'README must contain a current-version progress section.'
Need $changelog "(?m)^#\s+v$escaped\b" 'CHANGELOG must contain a current-version section.'
Need $readme '_AGENT_CONTEXT/PROJECT_PLAN\.md' 'README must link the canonical project plan.'

$plan=Active '_AGENT_CONTEXT/PROJECT_PLAN.md'
Need $plan '(?i)branch cleanup.{0,180}(?:work-extraction|extract)' 'Project plan must define extraction-first branch cleanup.'
Need $plan '(?im)^\|\s*RECOVERY-\d+' 'Project plan must retain actionable recovery items.'
Need $plan '(?i)ARCHIVED.{0,140}(?:not|non-terminal)' 'Project plan must state that archive-only preservation is non-terminal.'

$agents=Active 'AGENTS.md'
Need $agents 'fengie/heaven-toolbox@main' 'AGENTS must route global bootstrap to exact Heaven Toolbox main.'
Need $agents '_AGENT_CONTEXT/CURRENT_REVISION\.json' 'AGENTS must link current revision.'
Need $agents '_AGENT_CONTEXT/CONTINUITY_PROTOCOL\.md' 'AGENTS must link the permanent constitution.'
Need $agents '_AGENT_CONTEXT/PROJECT_PLAN\.md' 'AGENTS must link the canonical project plan.'
Need $agents 'LEARNED_RULES\.md' 'AGENTS must link active Learned Rules.'
Need $agents '(?i)task-relevant' 'AGENTS must use progressive task-relevant context.'
Need $agents '(?i)smallest coherent' 'AGENTS must favor smallest coherent implementation.'
Need $agents '(?i)narrowest useful' 'AGENTS must specify risk-calibrated verification.'
Need $agents '(?i)without private chat history' 'AGENTS must require chat-independent handoff.'
Need $agents '(?i)explicit user authorization' 'AGENTS must protect Core Rules.'
Need $agents '(?is)README\.md.{0,700}CHANGELOG\.md.{0,700}VERSION\.txt' 'AGENTS must preserve visible progress/version coordination.'
Need $agents '(?im)^Before any task-specific reasoning, answering, planning, dispatch, or action, every agent and recurring worker must complete this bootstrap:' 'AGENTS must require Toolbox bootstrap before task reasoning, answer, plan, dispatch or action.'
Need $agents '(?is)Read.{0,80}_AGENT_CONTEXT/CONTINUITY_PROTOCOL\.md.{0,80}in full at startup' 'AGENTS must require the full continuity constitution at startup.'
Forbid $agents '(?is)fengie/mhw-mods.{0,120}(?:global training bootstrap authority|cross-repository programming-agent training baseline)' 'MHW must not claim global training/bootstrap ownership.'
Forbid $agents '(?is)Core Rules.{0,120}(?:may|can|should).{0,120}(?:weaken|override|change).{0,120}without\s+explicit\s+user\s+authorization' 'Core Rules cannot be weakened without explicit user authorization.'
Continuity $agents 'AGENTS'

$router=Active '_AGENT_CONTEXT/README_FIRST.md'
Need $router '_AGENT_CONTEXT/CURRENT_REVISION\.json' 'Context router must link current revision.'
Need $router '_AGENT_CONTEXT/CONTINUITY_PROTOCOL\.md' 'Context router must link constitution.'
Need $router 'LEARNED_RULES\.md' 'Context router must link Learned Rules.'
Need $router '_AGENT_CONTEXT/PROJECT_PLAN\.md' 'Context router must link the canonical project plan.'
Need $router '(?i)task-relevant' 'Context router must require task-relevant retrieval.'

$start=Active 'NEXT-AGENT-START-HERE.md'
Need $start "(?i)v$escaped\b" 'Current handoff must identify current version.'
Need $start 'fengie/heaven-toolbox@main' 'Current handoff must preserve Toolbox-first bootstrap.'
Need $start '(?i)successor' 'Current handoff must name successor continuity.'
Need $start '(?i)verification' 'Current handoff must carry verification state.'
Need $start '(?i)risk' 'Current handoff must carry unresolved risk.'
Need $start '_AGENT_CONTEXT/CONTINUITY_PROTOCOL\.md' 'Current handoff must link the permanent constitution.'
Need $start 'LEARNED_RULES\.md' 'Current handoff must link active Learned Rules.'
Continuity $start 'Current handoff'

$constitution=Active '_AGENT_CONTEXT/CONTINUITY_PROTOCOL.md'
Need $constitution '(?i)Core Rule' 'Continuity constitution must protect Core Rules.'
Need $constitution '(?i)recursive and indefinite' 'Continuity constitution must preserve recursive propagation.'
Need $constitution '(?i)exact verification' 'Continuity constitution must preserve exact verification.'
Need $constitution '(?i)SQLite transaction' 'Continuity constitution must preserve SQLite transaction discipline.'
Need $constitution '(?i)append-only' 'Continuity constitution must preserve Learned Rules audit history.'
Need $constitution '(?i)explicit user authorization' 'Continuity constitution must protect changes to Core Rules.'
Need $constitution '(?i)preservation mode' 'Continuity constitution must preserve resource-low recovery.'
Continuity $constitution 'Continuity constitution'

$learned=Active '_AGENT_CONTEXT/LEARNED_RULES.md'
foreach($concept in @('append-only','Rule ID','Active','Superseded','Core Rules')){Need $learned ([regex]::Escape($concept)) "Learned Rules must preserve $concept."}

$total=0
$total+=MaxBytes 'AGENTS.md' 9000
$total+=MaxBytes 'NEXT-AGENT-START-HERE.md' 7000
$total+=MaxBytes '_AGENT_CONTEXT/README_FIRST.md' 5500
if($total -gt 21500){throw "Active MHW routing/context set is $total bytes; 21500-byte regression budget exceeded."}

foreach($relative in @($manifest.requiredContextFiles)+@($manifest.requiredVerificationFiles)+@($manifest.requiredToolingFiles)){
    if([string]::IsNullOrWhiteSpace([string]$relative)){continue}
    $path=Join-Path $Root ([string]$relative).Replace([char]47,[char]92)
    if(-not (Test-Path -LiteralPath $path)){throw "Manifest-required local file missing: $relative"}
}

$functionStatus=Active '.verification/function-status.json' | ConvertFrom-Json
$functionIds=@{}
foreach($entry in @($functionStatus.functions)){
    if($functionIds.ContainsKey([string]$entry.id)){throw "Duplicate function id: $($entry.id)"}
    $functionIds[[string]$entry.id]=$true
    if([string]::IsNullOrWhiteSpace([string]$entry.fingerprint)){throw "Missing fingerprint: $($entry.id)"}
}
$stageStatus=Active '.verification/stage-status.json' | ConvertFrom-Json
$stageIds=@{}
foreach($entry in @($stageStatus.stages)){
    if($stageIds.ContainsKey([string]$entry.id)){throw "Duplicate stage id: $($entry.id)"}
    $stageIds[[string]$entry.id]=$true
    if([string]::IsNullOrWhiteSpace([string]$entry.fingerprint)){throw "Missing stage fingerprint: $($entry.id)"}
}

& (Join-Path $Root 'scripts\testing\Test-HeavenToolboxOwnership.ps1') -Root $Root
Write-Host ("PASS: MHW project governance preflight. Version="+$version+"; activeBytes="+$total+"; globalBootstrap=fengie/heaven-toolbox@main") -ForegroundColor Green
