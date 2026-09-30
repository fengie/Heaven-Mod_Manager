param([string]$Root)
$ErrorActionPreference='Stop'
if([string]::IsNullOrWhiteSpace($Root)){$Root=(Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path}else{$Root=(Resolve-Path $Root).Path}

function Active([string]$Relative){
    $p=Join-Path $Root ($Relative.Replace([char]47,[char]92))
    if(-not (Test-Path -LiteralPath $p)){throw "Missing required file: $Relative"}
    return [regex]::Replace((Get-Content -Raw -LiteralPath $p),'(?s)<!--.*?-->',' ')
}
function Need([string]$Text,[string]$Pattern,[string]$Message){if($Text -notmatch $Pattern){throw $Message}}
function Forbid([string]$Text,[string]$Pattern,[string]$Message){if($Text -match $Pattern){throw $Message}}
function MaxBytes([string]$Relative,[int]$Limit){
    $text=Active $Relative
    $bytes=[Text.Encoding]::UTF8.GetByteCount($text)
    if($bytes -gt $Limit){throw "$Relative is $bytes UTF-8 bytes; limit is $Limit."}
    return $bytes
}

$manifest=Active '_AGENT_CONTEXT/handoff-manifest.json' | ConvertFrom-Json
if($manifest.formatVersion -ne 1 -or $manifest.continuityRequired -ne $true -or $manifest.propagateToNextAgent -ne $true){throw 'Invalid handoff manifest continuity contract.'}
if([string]$manifest.canonicalRepository -ne 'fengie/mhw-mods' -or [string]$manifest.canonicalBranch -ne 'main'){throw 'Unexpected canonical repository/branch.'}

$version=(Active 'VERSION.txt').Trim()
[xml]$props=Active 'Directory.Build.props'
if([string]$props.Project.PropertyGroup.Version -ne $version){throw 'Directory.Build.props version must match VERSION.txt.'}
if([string]$manifest.currentVersion -ne $version){throw 'handoff-manifest currentVersion must match VERSION.txt.'}
$revision=Active '_AGENT_CONTEXT/CURRENT_REVISION.json' | ConvertFrom-Json
if([string]$revision.currentVersion -ne $version){throw 'CURRENT_REVISION currentVersion must match VERSION.txt.'}
if([string]::IsNullOrWhiteSpace([string]$revision.verificationAppliesToCommit)){throw 'CURRENT_REVISION must identify verificationAppliesToCommit.'}

$escaped=[regex]::Escape($version)
$readme=Active 'README.md'; $changelog=Active 'CHANGELOG.md'
Need $readme "(?m)^#\s+v$escaped\b" 'README title must show current version.'
Need $readme "(?m)^##\s+v$escaped\b" 'README must contain a current-version progress section.'
Need $changelog "(?m)^#\s+v$escaped\b" 'CHANGELOG must contain a current-version section.'

$agents=Active 'AGENTS.md'
Need $agents '(?i)canonical working state' 'AGENTS must identify canonical working state.'
Need $agents '_AGENT_TRAINING/README\.md' 'AGENTS must link the trainer router.'
Need $agents '_AGENT_TRAINING/AGENT_OPERATING_STANDARD\.md' 'AGENTS must link the universal operating standard.'
Need $agents '_AGENT_CONTEXT/CURRENT_REVISION\.json' 'AGENTS must link current revision.'
Need $agents '_AGENT_CONTEXT/CONTINUITY_PROTOCOL\.md' 'AGENTS must link the permanent constitution.'
Need $agents 'GLOBAL_GIT_DIRECTIVE\.md' 'AGENTS must link canonical Git policy.'
Need $agents '(?i)task-relevant' 'AGENTS must use progressive task-relevant context.'
Need $agents '(?i)smallest coherent' 'AGENTS must favor smallest coherent implementation.'
Need $agents '(?i)narrowest useful' 'AGENTS must specify risk-calibrated verification.'
Need $agents '(?i)without private chat history' 'AGENTS must require chat-independent handoff.'
Need $agents '(?i)explicit user authorization' 'AGENTS must protect Core Rules.'

$standard=Active '_AGENT_TRAINING/AGENT_OPERATING_STANDARD.md'
Need $standard '(?is)inspect.*understand.*implement.*test.*verify.*integrate.*hand off' 'Operating standard must define execution order.'
Need $standard '(?i)smallest coherent change' 'Operating standard must constrain scope.'
Need $standard '(?i)reproduce.*defect|defect.*reproduce' 'Operating standard must encourage bug reproduction.'
Need $standard '(?i)flaky tests' 'Operating standard must treat flaky tests as signals.'
Need $standard '(?i)private chat history' 'Operating standard must define durable completion.'

$trainer=Active '_AGENT_TRAINING/README.md'
Need $trainer '(?is)delete.*merge.*rewrite.*relocate.*add' 'Trainer maintenance order must prefer consolidation before addition.'

$git=Active 'GLOBAL_GIT_DIRECTIVE.md'
Need $git '(?i)main.*canonical integration target' 'Git directive must identify canonical main.'
Need $git '(?i)never.*force-push' 'Git directive must forbid shared/canonical force push.'
Need $git '(?is)README\.md.*CHANGELOG\.md.*VERSION\.txt' 'Git directive must preserve visible patch/version coordination.'
Need $git '(?i)remote.*main' 'Git directive must require canonical remote-main verification.'

$router=Active '_AGENT_CONTEXT/README_FIRST.md'
Need $router '_AGENT_CONTEXT/CURRENT_REVISION\.json' 'Context router must link current revision.'
Need $router '_AGENT_CONTEXT/CONTINUITY_PROTOCOL\.md' 'Context router must link constitution.'
Need $router 'LEARNED_RULES\.md' 'Context router must link Learned Rules.'
Need $router '(?i)task-relevant' 'Context router must require task-relevant retrieval.'
Forbid $router '(?i)MHW Manual Mod Manager v8\.8\.7' 'Context router contains obsolete fixed-version startup text.'

$start=Active 'NEXT-AGENT-START-HERE.md'
Need $start "(?i)v$escaped\b" 'Current handoff must identify current version.'
Need $start '(?i)successor' 'Current handoff must name successor continuity.'
Need $start '(?i)verification' 'Current handoff must carry verification state.'
Need $start '(?i)risk' 'Current handoff must carry unresolved risk.'

$constitution=Active '_AGENT_CONTEXT/CONTINUITY_PROTOCOL.md'
Need $constitution '(?i)Core Rule' 'Continuity constitution must protect Core Rules.'
Need $constitution '(?i)recursive and indefinite' 'Continuity constitution must preserve recursive propagation.'
Need $constitution '(?i)exact verification' 'Continuity constitution must preserve exact verification.'
Need $constitution '(?i)SQLite transaction' 'Continuity constitution must preserve SQLite transaction discipline.'
Need $constitution '(?i)append-only' 'Continuity constitution must preserve Learned Rules audit history.'
Need $constitution '(?i)explicit user authorization' 'Continuity constitution must protect changes to Core Rules.'

$total=0
$total+=MaxBytes 'AGENTS.md' 9000
$total+=MaxBytes '_AGENT_TRAINING/README.md' 5500
$total+=MaxBytes '_AGENT_TRAINING/AGENT_OPERATING_STANDARD.md' 9000
$total+=MaxBytes '_AGENT_TRAINING/PROMPT_TEMPLATES/00_SWARM_RULES.txt' 6000
$total+=MaxBytes '_AGENT_TRAINING/PROMPT_TEMPLATES/01_MANAGER_ORCHESTRATOR.txt' 7000
$total+=MaxBytes 'NEXT-AGENT-START-HERE.md' 7000
$total+=MaxBytes '_AGENT_CONTEXT/README_FIRST.md' 5500
if($total -gt 45000){throw "Active training/router set is $total bytes; 45000-byte regression budget exceeded."}

foreach($relative in @($manifest.requiredContextFiles)+@($manifest.requiredVerificationFiles)+@($manifest.requiredToolingFiles)){
    if([string]::IsNullOrWhiteSpace([string]$relative)){continue}
    $path=Join-Path $Root ([string]$relative).Replace([char]47,[char]92)
    if(-not (Test-Path -LiteralPath $path)){throw "Manifest-required file missing: $relative"}
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

Write-Host ("PASS: compact agent governance preflight. Version="+$version+"; activeBytes="+$total) -ForegroundColor Green
