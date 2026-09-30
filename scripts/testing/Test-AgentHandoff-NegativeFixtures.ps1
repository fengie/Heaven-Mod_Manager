param([string]$Root)
$ErrorActionPreference='Stop'

if([string]::IsNullOrWhiteSpace($Root)){
    $Root=(Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
}else{
    $Root=(Resolve-Path $Root).Path
}

$manifest=Get-Content -Raw -LiteralPath (Join-Path $Root '_AGENT_CONTEXT\handoff-manifest.json') | ConvertFrom-Json
$fixture=Join-Path ([IO.Path]::GetTempPath()) ('mhw-agent-handoff-fixture-'+[Guid]::NewGuid().ToString('N'))

function Copy-FixtureFile {
    param([string]$Relative)
    if([string]::IsNullOrWhiteSpace($Relative)){return}
    $source=Join-Path $Root ($Relative.Replace([char]47,[char]92))
    $dest=Join-Path $fixture ($Relative.Replace([char]47,[char]92))
    $parent=Split-Path -Parent $dest
    if(-not (Test-Path $parent)){New-Item -ItemType Directory -Force -Path $parent | Out-Null}
    if($Relative.EndsWith('.zip',[StringComparison]::OrdinalIgnoreCase)){
        New-Item -ItemType File -Force -Path $dest | Out-Null
    }else{
        Copy-Item -LiteralPath $source -Destination $dest -Force
    }
}

function Expect-Rejected {
    param(
        [string]$Name,
        [string]$Relative,
        [ScriptBlock]$Mutate
    )
    $path=Join-Path $fixture ($Relative.Replace([char]47,[char]92))
    $original=Get-Content -Raw -LiteralPath $path
    try{
        $changed=& $Mutate $original
        if($changed -ceq $original){throw "Negative fixture '$Name' did not mutate its input; update the fixture for current policy prose."}
        Set-Content -LiteralPath $path -Value $changed -Encoding UTF8
        $rejected=$false
        try{
            & (Join-Path $fixture 'scripts\testing\Test-AgentHandoff.ps1') -Root $fixture *> $null
        }catch{
            $rejected=$true
        }
        if(-not $rejected){throw "Negative fixture '$Name' was incorrectly accepted by Test-AgentHandoff.ps1."}
        Write-Host "PASS: Negative handoff fixture rejected: $Name" -ForegroundColor Green
    }finally{
        Set-Content -LiteralPath $path -Value $original -Encoding UTF8
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
        '_AGENT_CONTEXT/CURRENT_REVISION.json',
        '_AGENT_CONTEXT/README_FIRST.md',
        '_AGENT_CONTEXT/CONTINUITY_PROTOCOL.md',
        '_AGENT_CONTEXT/LEARNED_RULES.md',
        '_AGENT_CONTEXT/handoff-manifest.json',
        '.verification/function-status.json',
        '.verification/stage-status.json'
    )
    $files += @($manifest.requiredContextFiles | ForEach-Object {[string]$_})
    $files += @($manifest.requiredVerificationFiles | ForEach-Object {[string]$_})
    $files += @($manifest.requiredToolingFiles | ForEach-Object {[string]$_})

    foreach($relative in ($files | Where-Object {-not [string]::IsNullOrWhiteSpace($_)} | Select-Object -Unique)){
        Copy-FixtureFile $relative
    }

    & (Join-Path $fixture 'scripts\testing\Test-AgentHandoff.ps1') -Root $fixture *> $null
    Write-Host 'PASS: Baseline handoff fixture accepted.' -ForegroundColor Green

    Expect-Rejected 'AGENTS loses visible-progress versioning rule' 'AGENTS.md' {
        param($text)
        [regex]::Replace($text,'(?ms)^### Mandatory visible-progress versioning\s*.*?(?=^### Mandatory completion handoff)','')
    }

    Expect-Rejected 'README loses current-patch progress section' 'README.md' {
        param($text)
        $escaped=[regex]::Escape([string]$manifest.currentVersion)
        [regex]::Replace($text,"(?m)^##\s+v$escaped\b.*$",'## previous-version progress only',1)
    }

    Expect-Rejected 'GLOBAL directive loses README patch rule' 'GLOBAL_GIT_DIRECTIVE.md' {
        param($text)
        [regex]::Replace($text,'(?ms)^# PROGRESS VISIBILITY / PATCH VERSION COORDINATION\s*.*?(?=^---\s*$)','')
    }

    Expect-Rejected 'AGENTS loses learned-rules entry' 'AGENTS.md' {
        param($text)
        $text -replace '(?im)^.*_AGENT_CONTEXT/LEARNED_RULES\.md.*\r?\n?',''
    }

    Expect-Rejected 'AGENTS loses recursive successor propagation' 'AGENTS.md' {
        param($text)
        ($text -replace '(?i)successor','future agent') -replace '(?i)agent after them','later agent'
    }

    Expect-Rejected 'start-here loses recursive propagation' 'NEXT-AGENT-START-HERE.md' {
        param($text)
        ($text -replace '(?i)successor','future agent') -replace '(?i)agent after them','later agent'
    }

    Expect-Rejected 'constitution loses Core Rule protection' '_AGENT_CONTEXT/CONTINUITY_PROTOCOL.md' {
        param($text)
        $text -replace '(?i)explicit user authorization','ordinary project decision'
    }

    Expect-Rejected 'AGENTS hides learned-rules requirement in a comment' 'AGENTS.md' {
        param($text)
        $line=[regex]::Match($text,'(?m)^2\. Read .*$').Value
        if([string]::IsNullOrWhiteSpace($line)){throw 'Fixture could not locate mandatory training read step 2.'}
        $changed=$line -replace '`?_AGENT_CONTEXT/LEARNED_RULES\.md`?','the continuity ledger'
        if($changed -ceq $line){throw 'Fixture failed to remove the active learned-rules requirement.'}
        return $text.Replace($line,$changed+' <!-- _AGENT_CONTEXT/LEARNED_RULES.md -->')
    }

    Expect-Rejected 'start-here explicitly negates successor propagation' 'NEXT-AGENT-START-HERE.md' {
        param($text)
        $text -replace '(?i)require your successor to propagate them recursively to the agent after them','require your successor must not preserve or propagate them to the agent after them'
    }

    Expect-Rejected 'AGENTS weakens Core Rules while retaining authorization keyword' 'AGENTS.md' {
        param($text)
        $text -replace '(?i)Core (?:continuity rules|Rules) (?:may|can) be weakened only with explicit user authorization\.','Core Rules may be weakened without explicit user authorization.'
    }

    Expect-Rejected 'README read-order deception' '_AGENT_CONTEXT/README_FIRST.md' {
        param($text)
        $protocolLine=[regex]::Match($text,'(?m)^2\. .*CONTINUITY_PROTOCOL\.md.*$').Value
        $learnedLine=[regex]::Match($text,'(?m)^3\. .*LEARNED_RULES\.md.*$').Value
        if([string]::IsNullOrWhiteSpace($protocolLine) -or [string]::IsNullOrWhiteSpace($learnedLine)){throw 'Fixture could not locate canonical read-order lines.'}
        $changed=$text.Replace($protocolLine,'__PROTOCOL_LINE__').Replace($learnedLine,$protocolLine).Replace('__PROTOCOL_LINE__',$learnedLine)
        return "Reference order: _AGENT_CONTEXT/CONTINUITY_PROTOCOL.md then _AGENT_CONTEXT/LEARNED_RULES.md.`r`n`r`n"+$changed
    }

    Write-Host 'PASS: Agent handoff negative fixtures prove recursive continuity checks fail closed.' -ForegroundColor Green
}finally{
    if(Test-Path $fixture){Remove-Item -LiteralPath $fixture -Recurse -Force -ErrorAction SilentlyContinue}
}
