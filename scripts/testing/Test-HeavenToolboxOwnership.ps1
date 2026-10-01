param([string]$Root = '')
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest

if([string]::IsNullOrWhiteSpace($Root)){
    $Root=(Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
}else{
    $Root=(Resolve-Path -LiteralPath $Root).Path
}

$forbiddenDirectories=@('_AGENT_TRAINING','plugins','heaven-bridge','tools')
$forbiddenFiles=@('GLOBAL_GIT_DIRECTIVE.md')
$violations=New-Object System.Collections.Generic.List[string]

foreach($relative in $forbiddenDirectories){
    $path=Join-Path $Root $relative
    if(Test-Path -LiteralPath $path){$violations.Add("Forbidden migrated global root exists in MHW: $relative/")}
}
foreach($relative in $forbiddenFiles){
    $path=Join-Path $Root $relative
    if(Test-Path -LiteralPath $path){$violations.Add("Forbidden migrated global file exists in MHW: $relative")}
}

$agentsPath=Join-Path $Root 'AGENTS.md'
if(!(Test-Path -LiteralPath $agentsPath)){throw 'AGENTS.md is missing.'}
$agents=Get-Content -LiteralPath $agentsPath -Raw
if(-not $agents.Contains('fengie/heaven-toolbox@main')){
    $violations.Add('AGENTS.md must require exact fengie/heaven-toolbox@main bootstrap.')
}
if($agents -match '(?is)fengie/mhw-mods.{0,120}(?:global training bootstrap authority|cross-repository programming-agent training baseline)'){
    $violations.Add('AGENTS.md must not claim MHW as the global/cross-repository training or toolbox authority.')
}

$readmePath=Join-Path $Root 'README.md'
$layoutPath=Join-Path $Root 'docs\REPOSITORY-LAYOUT.md'
foreach($requiredPath in @($readmePath,$layoutPath)){
    if(!(Test-Path -LiteralPath $requiredPath)){throw "Required cutover documentation is missing: $requiredPath"}
}
$readme=Get-Content -LiteralPath $readmePath -Raw
$layout=Get-Content -LiteralPath $layoutPath -Raw
if(-not $readme.Contains('fengie/heaven-toolbox@main')){
    $violations.Add('README.md must route reusable/global ownership to fengie/heaven-toolbox@main.')
}
foreach($stale in @(
    'New plugin development is centralized under `plugins/`',
    'The existing `heaven-bridge/` directory remains active compatibility/runtime infrastructure'
)){
    if($readme.Contains($stale)){
        $violations.Add("README.md contains stale pre-cutover routing: $stale")
    }
}
foreach($staleLayout in @(
    '- `tools/` — developer utilities and standalone helper programs.',
    '- `plugins/` — plugin implementations and plugin-specific docs/tests.',
    '- `_AGENT_TRAINING/` — durable agent operating standards and prompt templates.',
    '../_AGENT_TRAINING/REPOSITORY_STRUCTURE.md'
)){
    if($layout.Contains($staleLayout)){
        $violations.Add("docs/REPOSITORY-LAYOUT.md contains stale pre-cutover routing: $staleLayout")
    }
}
if(-not $layout.Contains('fengie/heaven-toolbox@main')){
    $violations.Add('docs/REPOSITORY-LAYOUT.md must identify Heaven Toolbox as reusable/global owner.')
}

$manifestPath=Join-Path $Root '_AGENT_CONTEXT\handoff-manifest.json'
if(!(Test-Path -LiteralPath $manifestPath)){throw 'handoff-manifest.json is missing.'}
$manifest=Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
if([string]$manifest.globalBootstrapRepository -ne 'fengie/heaven-toolbox'){
    $violations.Add('handoff-manifest globalBootstrapRepository must be fengie/heaven-toolbox.')
}
if([string]$manifest.globalBootstrapBranch -ne 'main'){
    $violations.Add('handoff-manifest globalBootstrapBranch must be main.')
}
if([string]$manifest.companyTrainer -notmatch '^fengie/heaven-toolbox@main:'){
    $violations.Add('handoff-manifest companyTrainer must route to Heaven Toolbox main.')
}

if($violations.Count -gt 0){
    foreach($item in $violations){Write-Host " - $item" -ForegroundColor Red}
    throw "Heaven Toolbox ownership gate failed with $($violations.Count) violation(s)."
}
Write-Host 'PASS: Heaven Toolbox is the exclusive global bootstrap/tooling owner; MHW contains project-only state.' -ForegroundColor Green
