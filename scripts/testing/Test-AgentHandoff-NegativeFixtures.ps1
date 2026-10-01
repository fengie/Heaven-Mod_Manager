param([string]$Root)
$ErrorActionPreference='Stop'
if([string]::IsNullOrWhiteSpace($Root)){$Root=(Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path}else{$Root=(Resolve-Path $Root).Path}

$manifest=Get-Content -Raw -Encoding UTF8 -LiteralPath (Join-Path $Root '_AGENT_CONTEXT\handoff-manifest.json') | ConvertFrom-Json
$fixture=Join-Path ([IO.Path]::GetTempPath()) ('mhw-governance-fixture-'+[Guid]::NewGuid().ToString('N'))

function Copy-FixtureFile {
    param([string]$Relative)
    if([string]::IsNullOrWhiteSpace($Relative)){return}
    $source=Join-Path $Root ($Relative.Replace([char]47,[char]92))
    $dest=Join-Path $fixture ($Relative.Replace([char]47,[char]92))
    $parent=Split-Path -Parent $dest
    if(-not (Test-Path -LiteralPath $parent)){New-Item -ItemType Directory -Force -Path $parent | Out-Null}
    Copy-Item -LiteralPath $source -Destination $dest -Force
}

function Reject {
    param([string]$Name,[string]$Relative,[scriptblock]$Mutate)
    $path=Join-Path $fixture ($Relative.Replace([char]47,[char]92))
    $originalBytes=[IO.File]::ReadAllBytes($path)
    $original=(New-Object Text.UTF8Encoding($false,$true)).GetString($originalBytes).TrimStart([char]0xFEFF)
    try{
        $changed=& $Mutate $original
        if($changed -ceq $original){throw "Fixture '$Name' did not change its target."}
        Set-Content -LiteralPath $path -Value $changed -Encoding utf8
        $rejected=$false
        try{& (Join-Path $fixture 'scripts\testing\Test-AgentHandoff.ps1') -Root $fixture *> $null}catch{$rejected=$true}
        if(-not $rejected){throw "Negative fixture was accepted: $Name"}
        Write-Host "PASS: rejected $Name" -ForegroundColor Green
    }finally{
        [IO.File]::WriteAllBytes($path,$originalBytes)
    }
}

try{
    New-Item -ItemType Directory -Force -Path $fixture | Out-Null
    $files=@(
        'VERSION.txt',
        'Directory.Build.props',
        'README.md',
        'CHANGELOG.md',
        'GLOBAL_GIT_DIRECTIVE.md',
        'AGENTS.md',
        'NEXT-AGENT-START-HERE.md',
        '_AGENT_TRAINING/README.md',
        '_AGENT_TRAINING/AGENT_OPERATING_STANDARD.md',
        '_AGENT_TRAINING/PROMPT_TEMPLATES/00_SWARM_RULES.txt',
        '_AGENT_TRAINING/PROMPT_TEMPLATES/01_MANAGER_ORCHESTRATOR.txt',
        '_AGENT_CONTEXT/CURRENT_REVISION.json',
        '_AGENT_CONTEXT/README_FIRST.md',
        '_AGENT_CONTEXT/CONTINUITY_PROTOCOL.md',
        '_AGENT_CONTEXT/handoff-manifest.json',
        '.verification/function-status.json',
        '.verification/stage-status.json',
        'scripts/testing/Test-AgentHandoff.ps1'
    )
    $files += @($manifest.requiredContextFiles | ForEach-Object {[string]$_})
    $files += @($manifest.requiredVerificationFiles | ForEach-Object {[string]$_})
    $files += @($manifest.requiredToolingFiles | ForEach-Object {[string]$_})

    foreach($relative in ($files | Where-Object {-not [string]::IsNullOrWhiteSpace($_)} | Select-Object -Unique)){
        Copy-FixtureFile $relative
    }

    & (Join-Path $fixture 'scripts\testing\Test-AgentHandoff.ps1') -Root $fixture *> $null
    Write-Host 'PASS: baseline compact governance fixture accepted.' -ForegroundColor Green

    Reject 'AGENTS loses operating-standard owner' 'AGENTS.md' {param($x) $x -replace '_AGENT_TRAINING/AGENT_OPERATING_STANDARD\.md','operating standard'}
    Reject 'AGENTS loses progressive retrieval' 'AGENTS.md' {param($x) $x -replace 'task-relevant','all-context'}
    Reject 'training vocabulary cannot delay bootstrap until mutation or completion' 'AGENTS.md' {
        param($x)
        [regex]::Replace($x,'(?im)^Before\s+.*bootstrap.*$','Training covers task-specific reasoning, answering, planning, dispatch and action, but every agent must complete the bootstrap only before mutation or completion.')
    }
    Reject 'full constitution startup cannot become task-only reading' 'AGENTS.md' {
        param($x)
        $x -replace '(?i)(CONTINUITY_PROTOCOL\.md`?\s+)in full\s+at startup','$1only task-relevant sections when needed'
    }
    Reject 'training obligation cannot be negated with timing vocabulary retained' 'AGENTS.md' {
        param($x)
        [regex]::Replace($x,'(?im)^(Before\s+.*bootstrap.*)$',{param($match) $match.Value -replace '\bmust\b','must not'})
    }
    Reject 'full constitution startup cannot become optional' 'AGENTS.md' {
        param($x)
        $x -replace '(?i)(CONTINUITY_PROTOCOL\.md`?\s+)in full\s+at startup','$1optionally at startup'
    }
    Reject 'operating standard loses smallest-change rule' '_AGENT_TRAINING/AGENT_OPERATING_STANDARD.md' {param($x) $x -replace 'smallest coherent change','broad change'}
    Reject 'Git policy permits force push' 'GLOBAL_GIT_DIRECTIVE.md' {param($x) $x -replace 'Never overwrite another owner''s changes or force-push shared/canonical history\.','Force-push shared history when convenient.'}
    Reject 'current handoff loses version' 'NEXT-AGENT-START-HERE.md' {param($x) $v=(Get-Content -Raw -Encoding UTF8 -LiteralPath (Join-Path $fixture 'VERSION.txt')).Trim(); $x -replace ("v"+[regex]::Escape($v)),'version-current'}
    Reject 'constitution loses exact verification' '_AGENT_CONTEXT/CONTINUITY_PROTOCOL.md' {param($x) $x -replace '(?i)exact verification','approximate evidence'}
    Reject 'project plan loses recovery queue' '_AGENT_CONTEXT/PROJECT_PLAN.md' {param($x) [regex]::Replace($x,'(?m)^\|\s*RECOVERY-.*$','')}
    Reject 'project plan loses archive non-terminal rule' '_AGENT_CONTEXT/PROJECT_PLAN.md' {param($x) $x -replace '(?i)`ARCHIVED` by itself is only a preservation mechanism and is \*\*not\*\* a terminal work disposition','`ARCHIVED` by itself is a terminal work disposition'}
    Reject 'Git cleanup loses explicit extraction disposition' 'GLOBAL_GIT_DIRECTIVE.md' {param($x) $x -replace '(?i)`INTEGRATED`, `EXTRACTED`, `SUPERSEDED`, or `REJECTED`','`ARCHIVED`'}
    Reject 'AGENTS exceeds context budget' 'AGENTS.md' {param($x) $x + ("noise" * 3000)}
    Reject 'HTML comments cannot evade actual source byte budget' 'AGENTS.md' {param($x) $x + '<!--' + ('noise' * 3000) + '-->'}
    foreach($relative in @('AGENTS.md','NEXT-AGENT-START-HERE.md')){
        Reject "$relative preserves vocabulary but removes successor obligation" $relative {
            param($x)
            [regex]::Replace($x,'(?im)^.*\b(?:successor|next agent)\b.*\b(?:must|shall|is required to)\b.*\b(?:propagate|pass)\b.*$','Successor, recursive propagation, constitution, and the agent after them are descriptive vocabulary only.')
        }
        Reject "$relative negates recursive obligation" $relative {param($x) $x -replace '(?i)successor must','successor must not'}
    }
    Reject 'Core authorization vocabulary cannot permit weakening' 'AGENTS.md' {param($x) $x + "`nCore Rules may be weakened without explicit user authorization."}
    Reject 'active Learned Rules reference cannot hide in comments' 'AGENTS.md' {param($x) ($x -replace 'LEARNED_RULES\.md','continuity-ledger') + '<!-- LEARNED_RULES.md -->'}
    Reject 'README loses current progress heading' 'README.md' {param($x) $v=[regex]::Escape([string]$manifest.currentVersion); $x -replace ("(?m)^##\s+v"+$v+'\b.*$'),'## historical progress'}
    Reject 'constitution loses Core authorization protection' '_AGENT_CONTEXT/CONTINUITY_PROTOCOL.md' {param($x) $x -replace '(?i)explicit user authorization','ordinary project decision'}

    Write-Host 'PASS: compact governance negative fixtures fail closed.' -ForegroundColor Green
}finally{
    if(Test-Path -LiteralPath $fixture){Remove-Item -LiteralPath $fixture -Recurse -Force -ErrorAction SilentlyContinue}
}
