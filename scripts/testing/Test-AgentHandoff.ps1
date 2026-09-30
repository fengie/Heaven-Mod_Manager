param([string]$Root)
$ErrorActionPreference='Stop'

if([string]::IsNullOrWhiteSpace($Root)){
    $Root=(Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
}else{
    $Root=(Resolve-Path $Root).Path
}

function Assert-Match {
    param(
        [string]$Text,
        [string]$Pattern,
        [string]$Message
    )
    if($Text -notmatch $Pattern){throw $Message}
}

function Assert-NoMatch {
    param(
        [string]$Text,
        [string]$Pattern,
        [string]$Message
    )
    if($Text -match $Pattern){throw $Message}
}

function Get-ActiveMarkdownText {
    param([string]$Text)
    return [regex]::Replace($Text,'(?s)<!--.*?-->',' ')
}

$manifestPath=Join-Path $Root '_AGENT_CONTEXT\handoff-manifest.json'
if(-not (Test-Path $manifestPath)){throw "Agent handoff manifest is missing: $manifestPath"}
$manifest=Get-Content -Raw -Path $manifestPath | ConvertFrom-Json
if($manifest.formatVersion -ne 1){throw "Unsupported agent handoff manifest format: $($manifest.formatVersion)"}
if($manifest.continuityRequired -ne $true){throw 'handoff-manifest.json must keep continuityRequired=true.'}
if($manifest.propagateToNextAgent -ne $true){throw 'handoff-manifest.json must keep propagateToNextAgent=true.'}
if([string]$manifest.canonicalRepository -ne 'fengie/mhw-mods'){throw "Unexpected canonical repository: $($manifest.canonicalRepository)"}
if([string]$manifest.canonicalBranch -ne 'main'){throw "Unexpected canonical branch: $($manifest.canonicalBranch)"}
if([string]$manifest.agentInstructions -ne 'AGENTS.md'){throw 'handoff-manifest.json must point agentInstructions to AGENTS.md.'}
if([string]$manifest.currentRevisionFile -ne '_AGENT_CONTEXT/CURRENT_REVISION.json'){throw 'handoff-manifest.json must point currentRevisionFile to CURRENT_REVISION.json.'}
if([string]$manifest.continuityProtocol -ne '_AGENT_CONTEXT/CONTINUITY_PROTOCOL.md'){throw 'handoff-manifest.json must point continuityProtocol to CONTINUITY_PROTOCOL.md.'}
if([string]$manifest.learnedRules -ne '_AGENT_CONTEXT/LEARNED_RULES.md'){throw 'handoff-manifest.json must point learnedRules to LEARNED_RULES.md.'}

$version=(Get-Content -Raw -Path (Join-Path $Root 'VERSION.txt')).Trim()
if([string]$manifest.currentVersion -ne [string]$version){throw "Agent handoff manifest version '$($manifest.currentVersion)' does not match VERSION.txt '$version'."}

$escapedVersion=[regex]::Escape($version)
$buildPropsPath=Join-Path $Root 'Directory.Build.props'
[xml]$buildProps=Get-Content -Raw -Path $buildPropsPath
$buildVersion=[string]$buildProps.Project.PropertyGroup.Version
if($buildVersion -ne [string]$version){throw "Directory.Build.props Version '$buildVersion' does not match VERSION.txt '$version'."}
$projectReadme=Get-ActiveMarkdownText (Get-Content -Raw -Path (Join-Path $Root 'README.md'))
Assert-Match $projectReadme ("(?m)^#\s+v$escapedVersion\b") "README.md title must show current VERSION.txt value '$version'."
Assert-Match $projectReadme ("(?m)^##\s+v$escapedVersion\b") "README.md must contain a current-patch progress section for '$version'."
$projectChangelog=Get-ActiveMarkdownText (Get-Content -Raw -Path (Join-Path $Root 'CHANGELOG.md'))
Assert-Match $projectChangelog ("(?m)^#\s+v$escapedVersion\b") "CHANGELOG.md must contain a current-patch section for '$version'."
$gitDirective=Get-ActiveMarkdownText (Get-Content -Raw -Path (Join-Path $Root 'GLOBAL_GIT_DIRECTIVE.md'))
Assert-Match $gitDirective '(?is)README\.md.{0,700}patch|patch.{0,700}README\.md' 'GLOBAL_GIT_DIRECTIVE.md must require README progress reporting and patch advancement.'

$required=@()
$required += [string]$manifest.startHere
$required += [string]$manifest.continuityProtocol
$required += [string]$manifest.learnedRules
$required += @($manifest.requiredContextFiles | ForEach-Object {[string]$_})
$required += @($manifest.requiredVerificationFiles | ForEach-Object {[string]$_})
if($null -ne $manifest.requiredToolingFiles){$required += @($manifest.requiredToolingFiles | ForEach-Object {[string]$_})}

$missing=@()
foreach($relative in ($required | Where-Object {-not [string]::IsNullOrWhiteSpace($_)} | Select-Object -Unique)){
    $path=Join-Path $Root ($relative.Replace([char]47, [char]92))
    if(-not (Test-Path $path)){$missing += $relative}
}
if($missing.Count -gt 0){throw ('Agent handoff is incomplete. Missing: '+($missing -join ', '))}

$revisionPath=Join-Path $Root '_AGENT_CONTEXT\CURRENT_REVISION.json'
$revision=Get-Content -Raw -Path $revisionPath | ConvertFrom-Json
if([int]$revision.formatVersion -ne 1){throw "Unsupported CURRENT_REVISION format: $($revision.formatVersion)"}
if([string]$revision.currentVersion -ne [string]$version){throw "CURRENT_REVISION version '$($revision.currentVersion)' does not match VERSION.txt '$version'."}
if([string]$revision.canonicalRepository -ne [string]$manifest.canonicalRepository){throw 'CURRENT_REVISION canonicalRepository does not match handoff manifest.'}
if([string]$revision.canonicalBranch -ne [string]$manifest.canonicalBranch){throw 'CURRENT_REVISION canonicalBranch does not match handoff manifest.'}
if([string]::IsNullOrWhiteSpace([string]$revision.verificationAppliesToCommit)){throw 'CURRENT_REVISION must identify verificationAppliesToCommit.'}
if([string]::IsNullOrWhiteSpace([string]$revision.status)){throw 'CURRENT_REVISION must contain a non-empty status.'}

$agents=Get-ActiveMarkdownText (Get-Content -Raw -Path (Join-Path $Root 'AGENTS.md'))
Assert-Match $agents '(?i)canonical (working state|development state|source of truth)' 'AGENTS.md must identify the canonical repository state.'
Assert-Match $agents 'NEXT-AGENT-START-HERE\.md' 'AGENTS.md must direct agents to NEXT-AGENT-START-HERE.md.'
Assert-Match $agents '_AGENT_CONTEXT/CURRENT_REVISION\.json' 'AGENTS.md must direct agents to CURRENT_REVISION.json.'
Assert-Match $agents '_AGENT_CONTEXT/CONTINUITY_PROTOCOL\.md' 'AGENTS.md must direct agents to CONTINUITY_PROTOCOL.md.'
$trainingGateMatch=[regex]::Match($agents,'(?ms)^## Mandatory pre-response repository training gate\s*(?<body>.*?)(?=^##\s|\z)')
if(-not $trainingGateMatch.Success){throw 'AGENTS.md must contain the mandatory pre-response repository training gate.'}
$trainingGate=$trainingGateMatch.Groups['body'].Value
$trainingReadMatch=[regex]::Match($trainingGate,'(?ms)^2\.\s*(?<body>.*?)(?=^3\.\s|\z)')
if(-not $trainingReadMatch.Success){throw 'AGENTS.md mandatory training gate must contain ordered read step 2.'}
$trainingRead=$trainingReadMatch.Groups['body'].Value
Assert-Match $trainingRead '_AGENT_CONTEXT/LEARNED_RULES\.md' 'AGENTS.md mandatory training read step must include LEARNED_RULES.md.'
$trainingProtocolIndex=$trainingRead.IndexOf('_AGENT_CONTEXT/CONTINUITY_PROTOCOL.md',[StringComparison]::Ordinal)
$trainingLearnedIndex=$trainingRead.IndexOf('_AGENT_CONTEXT/LEARNED_RULES.md',[StringComparison]::Ordinal)
if($trainingProtocolIndex -lt 0 -or $trainingLearnedIndex -lt 0 -or $trainingProtocolIndex -gt $trainingLearnedIndex){throw 'AGENTS.md mandatory training read step must place CONTINUITY_PROTOCOL.md before LEARNED_RULES.md.'}
Assert-Match $agents '(?i)git status' 'AGENTS.md must require git status inspection.'
Assert-Match $agents '(?i)(history|diff)' 'AGENTS.md must require relevant history/diff inspection.'
Assert-Match $agents '(?i)successor' 'AGENTS.md must explicitly pass continuity to a successor.'
Assert-Match $agents '(?i)agent after' 'AGENTS.md must require the successor to propagate continuity again.'
Assert-Match $agents '(?i)without previous chat history' 'AGENTS.md must require chat-independent continuation.'
Assert-Match $agents '(?i)explicit user authorization' 'AGENTS.md must protect Core Rules from unauthorized weakening.'
Assert-Match $agents '(?i)Do not break the chain' 'AGENTS.md must preserve the continuity invariant.'
Assert-Match $agents '(?is)README\.md.{0,700}patch|patch.{0,700}README\.md' 'AGENTS.md must require README progress reporting and patch advancement.'
Assert-NoMatch $agents '(?is)(?:\bsuccessor\b|\bagent after\b).{0,100}(?:must|should|may|can)\s+not\b.{0,120}(?:inherit|preserve|propagate|obey)' 'AGENTS.md must not negate successor continuity propagation.'
Assert-NoMatch $agents '(?i)(?:Core continuity rules|Core Rules?).{0,100}(?:without explicit user authorization|do not require explicit user authorization)' 'AGENTS.md must not weaken Core Rules while retaining authorization keywords.'

$start=Get-ActiveMarkdownText (Get-Content -Raw -Path (Join-Path $Root 'NEXT-AGENT-START-HERE.md'))
Assert-Match $start '(?i)permanent continuity constitution' 'NEXT-AGENT-START-HERE.md must identify the permanent continuity constitution.'
Assert-Match $start '_AGENT_CONTEXT/CONTINUITY_PROTOCOL\.md' 'NEXT-AGENT-START-HERE.md must link the permanent continuity constitution.'
Assert-Match $start '_AGENT_CONTEXT/LEARNED_RULES\.md' 'NEXT-AGENT-START-HERE.md must link the Learned Rules ledger.'
Assert-Match $start '(?i)successor' 'NEXT-AGENT-START-HERE.md must explicitly pass continuity to the successor.'
Assert-Match $start '(?i)agent after' 'NEXT-AGENT-START-HERE.md must require recursive propagation beyond the successor.'
Assert-Match $start '(?i)Do not break the chain' 'NEXT-AGENT-START-HERE.md must preserve the continuity invariant.'
Assert-NoMatch $start '(?is)(?:\bsuccessor\b|\bagent after\b).{0,100}(?:must|should|may|can)\s+not\b.{0,120}(?:inherit|preserve|propagate|obey)' 'NEXT-AGENT-START-HERE.md must not negate recursive continuity propagation.'

$protocol=Get-ActiveMarkdownText (Get-Content -Raw -Path (Join-Path $Root '_AGENT_CONTEXT\CONTINUITY_PROTOCOL.md'))
Assert-Match $protocol '(?i)Core Rule' 'CONTINUITY_PROTOCOL.md must identify Core Rules.'
Assert-Match $protocol '(?i)recursive and indefinite' 'CONTINUITY_PROTOCOL.md must preserve indefinite recursive propagation.'
Assert-Match $protocol '(?i)successor' 'CONTINUITY_PROTOCOL.md must explicitly instruct successor propagation.'
Assert-Match $protocol '(?i)agent after' 'CONTINUITY_PROTOCOL.md must require propagation beyond the immediate successor.'
Assert-Match $protocol '(?i)explicit user authorization' 'CONTINUITY_PROTOCOL.md must protect Core Rules from unauthorized weakening.'
Assert-Match $protocol '(?i)preservation mode' 'CONTINUITY_PROTOCOL.md must define preservation mode.'
Assert-Match $protocol '(?i)exact verification' 'CONTINUITY_PROTOCOL.md must preserve exact-input verification semantics.'
Assert-Match $protocol '(?i)SQLite transaction' 'CONTINUITY_PROTOCOL.md must preserve SQLite transaction-boundary discipline.'
Assert-Match $protocol '(?i)append-only' 'CONTINUITY_PROTOCOL.md must define append-only Learned Rules history.'
Assert-Match $protocol '(?i)Do not break the chain' 'CONTINUITY_PROTOCOL.md must preserve the continuity invariant.'
Assert-Match $protocol '(?is)README\.md.{0,700}patch|patch.{0,700}README\.md' 'CONTINUITY_PROTOCOL.md must preserve the README + patch progress Core Rule.'
Assert-NoMatch $protocol '(?is)(?:\bsuccessor\b|\bagent after\b).{0,100}(?:must|should|may|can)\s+not\b.{0,120}(?:inherit|preserve|propagate|obey)' 'CONTINUITY_PROTOCOL.md must not negate recursive continuity propagation.'
Assert-NoMatch $protocol '(?i)Core Rules?\s+may\s+be\s+weakened\s+without explicit user authorization' 'CONTINUITY_PROTOCOL.md must not explicitly permit Core Rule weakening without authorization.'

$learned=Get-ActiveMarkdownText (Get-Content -Raw -Path (Join-Path $Root '_AGENT_CONTEXT\LEARNED_RULES.md'))
Assert-Match $learned '(?i)append-only' 'LEARNED_RULES.md must identify append-only history.'
Assert-Match $learned '(?i)Rule ID' 'LEARNED_RULES.md must define auditable Rule IDs.'
Assert-Match $learned '(?i)Active' 'LEARNED_RULES.md must define Active status.'
Assert-Match $learned '(?i)Superseded' 'LEARNED_RULES.md must define Superseded status.'
Assert-Match $learned '(?i)Core Rules' 'LEARNED_RULES.md must defer to Core Rules.'

$readme=Get-ActiveMarkdownText (Get-Content -Raw -Path (Join-Path $Root '_AGENT_CONTEXT\README_FIRST.md'))
$readOrderMatch=[regex]::Match($readme,'(?ms)^## Read order\s*(?<body>.*?)(?=^##\s|\z)')
if(-not $readOrderMatch.Success){throw 'README_FIRST.md must contain an active ## Read order section.'}
$readOrder=$readOrderMatch.Groups['body'].Value
Assert-Match $readOrder '(?m)^\s*\d+\.\s+.*_AGENT_CONTEXT/CONTINUITY_PROTOCOL\.md' 'README_FIRST.md read order must contain CONTINUITY_PROTOCOL.md as an ordered-list entry.'
Assert-Match $readOrder '(?m)^\s*\d+\.\s+.*_AGENT_CONTEXT/LEARNED_RULES\.md' 'README_FIRST.md read order must contain LEARNED_RULES.md as an ordered-list entry.'
$protocolIndex=$readOrder.IndexOf('_AGENT_CONTEXT/CONTINUITY_PROTOCOL.md',[StringComparison]::Ordinal)
$learnedIndex=$readOrder.IndexOf('_AGENT_CONTEXT/LEARNED_RULES.md',[StringComparison]::Ordinal)
if($protocolIndex -lt 0 -or $learnedIndex -lt 0){throw 'README_FIRST.md must include CONTINUITY_PROTOCOL.md and LEARNED_RULES.md in the active read order.'}
if($protocolIndex -gt $learnedIndex){throw 'README_FIRST.md must place the permanent continuity protocol before the Learned Rules ledger in the active read order.'}

$functionStatusPath=Join-Path $Root '.verification\function-status.json'
$functionStatus=Get-Content -Raw -Path $functionStatusPath | ConvertFrom-Json
if([int]$functionStatus.formatVersion -ne 1){throw "Unsupported function-status format: $($functionStatus.formatVersion)"}
$functionIds=@{}
foreach($entry in @($functionStatus.functions)){
    if($null -eq $entry){continue}
    if([string]::IsNullOrWhiteSpace([string]$entry.id)){throw 'function-status.json contains a function entry without an id.'}
    if($functionIds.ContainsKey([string]$entry.id)){throw "function-status.json contains duplicate function id: $($entry.id)"}
    $functionIds[[string]$entry.id]=$true
    if([string]::IsNullOrWhiteSpace([string]$entry.fingerprint)){throw "function-status.json entry '$($entry.id)' is missing its fingerprint."}
    if($entry.verified -ne $true -and $entry.verified -ne $false){throw "function-status.json entry '$($entry.id)' must have boolean verified state."}
}

$stageStatusPath=Join-Path $Root '.verification\stage-status.json'
$stageStatus=Get-Content -Raw -Path $stageStatusPath | ConvertFrom-Json
if([int]$stageStatus.formatVersion -ne 1){throw "Unsupported stage-status format: $($stageStatus.formatVersion)"}
$stageIds=@{}
foreach($entry in @($stageStatus.stages)){
    if($null -eq $entry){continue}
    if([string]::IsNullOrWhiteSpace([string]$entry.id)){throw 'stage-status.json contains a stage entry without an id.'}
    if($stageIds.ContainsKey([string]$entry.id)){throw "stage-status.json contains duplicate stage id: $($entry.id)"}
    $stageIds[[string]$entry.id]=$true
    if([string]::IsNullOrWhiteSpace([string]$entry.fingerprint)){throw "stage-status.json entry '$($entry.id)' is missing its fingerprint."}
    if($entry.verified -ne $true -and $entry.verified -ne $false){throw "stage-status.json entry '$($entry.id)' must have boolean verified state."}
}

Write-Host ("PASS: Agent handoff continuity preflight. Version="+$version+"; requiredFiles="+$required.Count) -ForegroundColor Green
