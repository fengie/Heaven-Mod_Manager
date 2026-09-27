param([string]$Root)
$ErrorActionPreference='Stop'
if([string]::IsNullOrWhiteSpace($Root)){$Root=(Resolve-Path (Join-Path $PSScriptRoot '..')).Path}
else{$Root=(Resolve-Path $Root).Path}

$manifestPath=Join-Path $Root '_AGENT_CONTEXT\handoff-manifest.json'
if(-not (Test-Path $manifestPath)){throw "Agent handoff manifest is missing: $manifestPath"}
$manifest=Get-Content -Raw -Path $manifestPath | ConvertFrom-Json
if($manifest.formatVersion -ne 1){throw "Unsupported agent handoff manifest format: $($manifest.formatVersion)"}
if($manifest.continuityRequired -ne $true){throw 'handoff-manifest.json must keep continuityRequired=true.'}
if($manifest.propagateToNextAgent -ne $true){throw 'handoff-manifest.json must keep propagateToNextAgent=true.'}

$version=(Get-Content -Raw -Path (Join-Path $Root 'VERSION.txt')).Trim()
if([string]$manifest.currentVersion -ne [string]$version){throw "Agent handoff manifest version '$($manifest.currentVersion)' does not match VERSION.txt '$version'."}

$required=@()
$required += [string]$manifest.startHere
$required += [string]$manifest.continuityProtocol
$required += @($manifest.requiredContextFiles | ForEach-Object {[string]$_})
$required += @($manifest.requiredVerificationFiles | ForEach-Object {[string]$_})
if($null -ne $manifest.requiredToolingFiles){$required += @($manifest.requiredToolingFiles | ForEach-Object {[string]$_})}
$missing=@()
foreach($relative in ($required | Where-Object {-not [string]::IsNullOrWhiteSpace($_)} | Select-Object -Unique)){
    $path=Join-Path $Root ($relative.Replace([char]47, [char]92))
    if(-not (Test-Path $path)){$missing += $relative}
}
if($missing.Count -gt 0){throw ('Agent handoff is incomplete. Missing: '+($missing -join ', '))}


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

$start=Get-Content -Raw -Path (Join-Path $Root 'NEXT-AGENT-START-HERE.md')
$protocol=Get-Content -Raw -Path (Join-Path $Root '_AGENT_CONTEXT\CONTINUITY_PROTOCOL.md')
if($start -notmatch '(?i)Do not break the chain'){throw 'NEXT-AGENT-START-HERE.md must preserve the continuity propagation instruction.'}
if($protocol -notmatch '(?i)next agent'){throw 'CONTINUITY_PROTOCOL.md must explicitly instruct the next agent.'}
if($protocol -notmatch '(?i)Do not break the chain'){throw 'CONTINUITY_PROTOCOL.md must preserve the continuity invariant.'}

Write-Host ("PASS: Agent handoff continuity preflight. Version="+$version+"; requiredFiles="+$required.Count) -ForegroundColor Green
