param([string]$Root)
$ErrorActionPreference='Stop'
if([string]::IsNullOrWhiteSpace($Root)){$Root=(Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path}else{$Root=(Resolve-Path $Root).Path}
$fixture=Join-Path ([IO.Path]::GetTempPath()) ('mhw-governance-fixture-'+[Guid]::NewGuid().ToString('N'))

function Copy-Tree([string]$Source,[string]$Destination){
    New-Item -ItemType Directory -Force -Path $Destination | Out-Null
    Copy-Item -Path (Join-Path $Source '*') -Destination $Destination -Recurse -Force
}
function Reject([string]$Name,[string]$Relative,[scriptblock]$Mutate){
    $path=Join-Path $fixture ($Relative.Replace([char]47,[char]92))
    $original=Get-Content -Raw -LiteralPath $path
    try{
        $changed=& $Mutate $original
        if($changed -ceq $original){throw "Fixture '$Name' did not change its target."}
        Set-Content -LiteralPath $path -Value $changed -Encoding utf8
        $rejected=$false
        try{& (Join-Path $fixture 'scripts\testing\Test-AgentHandoff.ps1') -Root $fixture *> $null}catch{$rejected=$true}
        if(-not $rejected){throw "Negative fixture was accepted: $Name"}
        Write-Host "PASS: rejected $Name" -ForegroundColor Green
    }finally{Set-Content -LiteralPath $path -Value $original -Encoding utf8}
}

try{
    Copy-Tree $Root $fixture
    & (Join-Path $fixture 'scripts\testing\Test-AgentHandoff.ps1') -Root $fixture *> $null
    Write-Host 'PASS: baseline compact governance fixture accepted.' -ForegroundColor Green

    Reject 'AGENTS loses operating-standard owner' 'AGENTS.md' {param($x) $x -replace '_AGENT_TRAINING/AGENT_OPERATING_STANDARD\.md','operating standard'}
    Reject 'AGENTS loses progressive retrieval' 'AGENTS.md' {param($x) $x -replace 'task-relevant','all-context'}
    Reject 'operating standard loses smallest-change rule' '_AGENT_TRAINING/AGENT_OPERATING_STANDARD.md' {param($x) $x -replace 'smallest coherent change','broad change'}
    Reject 'Git policy permits force push' 'GLOBAL_GIT_DIRECTIVE.md' {param($x) $x -replace 'Never overwrite another owner''s changes or force-push shared/canonical history\.','Force-push shared history when convenient.'}
    Reject 'current handoff loses version' 'NEXT-AGENT-START-HERE.md' {param($x) $v=(Get-Content -Raw -LiteralPath (Join-Path $fixture 'VERSION.txt')).Trim(); $x -replace ("v"+[regex]::Escape($v)),'version-current'}
    Reject 'constitution loses exact verification' '_AGENT_CONTEXT/CONTINUITY_PROTOCOL.md' {param($x) $x -replace '(?i)exact verification','approximate evidence'}
    Reject 'AGENTS exceeds context budget' 'AGENTS.md' {param($x) $x + ("noise" * 3000)}

    Write-Host 'PASS: compact governance negative fixtures fail closed.' -ForegroundColor Green
}finally{
    if(Test-Path -LiteralPath $fixture){Remove-Item -LiteralPath $fixture -Recurse -Force -ErrorAction SilentlyContinue}
}
