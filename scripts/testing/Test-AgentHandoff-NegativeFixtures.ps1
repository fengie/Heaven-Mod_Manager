param([string]$Root)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
if([string]::IsNullOrWhiteSpace($Root)){$Root=(Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path}else{$Root=(Resolve-Path -LiteralPath $Root).Path}

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
    }finally{[IO.File]::WriteAllBytes($path,$originalBytes)}
}
function Reject-ForbiddenRoot {
    param([string]$Name,[string]$Relative)
    $path=Join-Path $fixture ($Relative.Replace([char]47,[char]92))
    try{
        New-Item -ItemType Directory -Force -Path $path | Out-Null
        Set-Content -LiteralPath (Join-Path $path 'README.md') -Value 'stale global copy' -Encoding utf8
        $rejected=$false
        try{& (Join-Path $fixture 'scripts\testing\Test-AgentHandoff.ps1') -Root $fixture *> $null}catch{$rejected=$true}
        if(-not $rejected){throw "Negative fixture was accepted: $Name"}
        Write-Host "PASS: rejected $Name" -ForegroundColor Green
    }finally{if(Test-Path -LiteralPath $path){Remove-Item -LiteralPath $path -Recurse -Force -ErrorAction SilentlyContinue}}
}

try{
    New-Item -ItemType Directory -Force -Path $fixture | Out-Null
    $files=@(
        'VERSION.txt','Directory.Build.props','README.md','CHANGELOG.md','AGENTS.md','NEXT-AGENT-START-HERE.md',
        'docs/REPOSITORY-LAYOUT.md',
        '_AGENT_CONTEXT/CURRENT_REVISION.json','_AGENT_CONTEXT/README_FIRST.md','_AGENT_CONTEXT/CONTINUITY_PROTOCOL.md',
        '_AGENT_CONTEXT/handoff-manifest.json','.verification/function-status.json','.verification/stage-status.json',
        'scripts/testing/Test-AgentHandoff.ps1','scripts/testing/Test-HeavenToolboxOwnership.ps1','scripts/release/Sync-VerificationContinuity.ps1'
    )
    $files += @($manifest.requiredContextFiles | ForEach-Object {[string]$_})
    $files += @($manifest.requiredVerificationFiles | ForEach-Object {[string]$_})
    $files += @($manifest.requiredToolingFiles | ForEach-Object {[string]$_})
    foreach($relative in ($files | Where-Object {-not [string]::IsNullOrWhiteSpace($_)} | Select-Object -Unique)){Copy-FixtureFile $relative}

    & (Join-Path $fixture 'scripts\testing\Test-AgentHandoff.ps1') -Root $fixture *> $null
    Write-Host 'PASS: baseline MHW project-governance fixture accepted.' -ForegroundColor Green

    Reject 'AGENTS loses Heaven Toolbox authority' 'AGENTS.md' {param($x) $x -replace 'fengie/heaven-toolbox@main','fengie/toolbox-missing@main'}
    Reject 'AGENTS reclaims global training authority for MHW' 'AGENTS.md' {param($x) $x + [Environment]::NewLine + 'This fengie/mhw-mods repository is the global training bootstrap authority.'}
    Reject 'README routes plugin development back to MHW' 'README.md' {param($x) $x + [Environment]::NewLine + 'New plugin development is centralized under `plugins/`.'}
    Reject 'repository layout advertises local plugin ownership' 'docs/REPOSITORY-LAYOUT.md' {param($x) $x + [Environment]::NewLine + '- `plugins/` — plugin implementations and plugin-specific docs/tests.'}
    Reject 'AGENTS loses progressive retrieval' 'AGENTS.md' {param($x) $x -replace 'task-relevant','all-context'}
    Reject 'AGENTS loses smallest coherent rule' 'AGENTS.md' {param($x) $x -replace 'smallest coherent','broadest convenient'}
    Reject 'full constitution startup cannot become optional' 'AGENTS.md' {param($x) $x -replace '(?i)in full\s+at startup','optionally at startup'}
    Reject 'successor continuity cannot be negated' 'AGENTS.md' {param($x) $x -replace '(?i)successor must','successor must not'}
    Reject 'Core authorization vocabulary cannot permit weakening' 'AGENTS.md' {param($x) $x + [Environment]::NewLine + 'Core Rules may be weakened without explicit user authorization.'}
    Reject 'current handoff loses version' 'NEXT-AGENT-START-HERE.md' {param($x) $v=(Get-Content -Raw -Encoding UTF8 -LiteralPath (Join-Path $fixture 'VERSION.txt')).Trim(); $x -replace ("v"+[regex]::Escape($v)),'version-current'}
    Reject 'constitution loses exact verification' '_AGENT_CONTEXT/CONTINUITY_PROTOCOL.md' {param($x) $x -replace '(?i)exact verification','approximate evidence'}
    Reject 'project plan loses recovery queue' '_AGENT_CONTEXT/PROJECT_PLAN.md' {param($x) [regex]::Replace($x,'(?m)^\|\s*RECOVERY-.*$','')}
    Reject 'project plan loses archive non-terminal rule' '_AGENT_CONTEXT/PROJECT_PLAN.md' {param($x) $x -replace '(?i)ARCHIVED.{0,120}not.{0,120}terminal work disposition','ARCHIVED is a terminal work disposition'}
    Reject 'manifest cannot route trainer back to MHW' '_AGENT_CONTEXT/handoff-manifest.json' {param($x) $x -replace 'fengie/heaven-toolbox@main:_AGENT_TRAINING/README\.md','fengie/mhw-mods@main:_AGENT_TRAINING/README.md'}
    Reject 'current revision cannot lose post-integration semantics' '_AGENT_CONTEXT/CURRENT_REVISION.json' {param($x) $x -replace '"stateSemantics":\s*"post-integration-canonical"','"stateSemantics": "candidate"'}
    Reject 'current revision cannot ship candidate integration state' '_AGENT_CONTEXT/CURRENT_REVISION.json' {param($x) $x -replace '"integrationState":\s*"canonical-main"','"integrationState": "candidate"'}
    Reject 'current revision cannot ship a feature working branch' '_AGENT_CONTEXT/CURRENT_REVISION.json' {param($x) $x -replace '"workingBranch":\s*"main"','"workingBranch": "feature/stale-candidate"'}
    Reject 'current revision status cannot remain candidate state' '_AGENT_CONTEXT/CURRENT_REVISION.json' {param($x) $x -replace '"status":\s*"[^"]+"','"status": "v-current-release-candidate"'}
    Reject 'current revision cannot retain candidate source commit' '_AGENT_CONTEXT/CURRENT_REVISION.json' {param($x) $x -replace '"candidateSourceCommit":\s*null','"candidateSourceCommit": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"'}
    Reject 'current revision cannot retain an active PR' '_AGENT_CONTEXT/CURRENT_REVISION.json' {param($x) $x -replace '"activePullRequest":\s*null','"activePullRequest": 999'}
    Reject 'current revision cannot retain an active issue' '_AGENT_CONTEXT/CURRENT_REVISION.json' {param($x) $x -replace '"activeIssue":\s*null','"activeIssue": 999'}
    Reject 'AGENTS exceeds local routing budget' 'AGENTS.md' {param($x) $x + ('noise' * 3000)}
    Reject-ForbiddenRoot 'local training copy cannot return' '_AGENT_TRAINING'
    Reject-ForbiddenRoot 'local plugin toolbox cannot return' 'plugins'
    Reject-ForbiddenRoot 'local Heaven Bridge cannot return' 'heaven-bridge'
    Reject-ForbiddenRoot 'root tools cannot return' 'tools'

    # Once a current-version closure exists, every active continuity surface must agree with it.
    $fixtureVersion=(Get-Content -Raw -Encoding UTF8 -LiteralPath (Join-Path $fixture 'VERSION.txt')).Trim()
    $fixtureSource='0123456789abcdef0123456789abcdef01234567'
    $fixtureRun='424242'
    $fixtureEvidenceRelative="_AGENT_CONTEXT/EVIDENCE/v$fixtureVersion-heaven-windows-closure.log"
    $fixtureEvidencePath=Join-Path $fixture ($fixtureEvidenceRelative.Replace([char]47,[char]92))
    $fixtureEvidenceParent=Split-Path -Parent $fixtureEvidencePath
    if(-not (Test-Path -LiteralPath $fixtureEvidenceParent)){New-Item -ItemType Directory -Force -Path $fixtureEvidenceParent | Out-Null}
    @"
MHW Manual Mod Manager v$fixtureVersion Heaven Windows closure
source_sha=$fixtureSource
run_id=$fixtureRun
runner_os=Windows

=== VERIFICATION REPORT ===
Overall: **PASS** - 26 passed / 0 failed
"@ | Set-Content -LiteralPath $fixtureEvidencePath -Encoding utf8
    & (Join-Path $fixture 'scripts\release\Sync-VerificationContinuity.ps1') -Root $fixture -SourceSha $fixtureSource -RunId $fixtureRun -EvidencePath $fixtureEvidenceRelative *> $null
    & (Join-Path $fixture 'scripts\testing\Test-AgentHandoff.ps1') -Root $fixture *> $null
    Write-Host 'PASS: synthetic current-version closure fixture accepted after continuity synchronization.' -ForegroundColor Green

    Reject 'current closure cannot retain stale verification SHA' '_AGENT_CONTEXT/CURRENT_REVISION.json' {param($x) $x -replace '"verificationAppliesToCommit":\s*"[^"]+"','"verificationAppliesToCommit": "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb"'}
    Reject 'current state cannot call closed current version a candidate' '_AGENT_CONTEXT/CURRENT_STATE.md' {param($x) $v=(Get-Content -Raw -Encoding UTF8 -LiteralPath (Join-Path $fixture 'VERSION.txt')).Trim(); [regex]::Replace($x,'(?ms)^## Verification boundary\s*.*?(?=^## |\z)',"## Verification boundary`r`n`r`nv$v requires fresh exact-source gates for the final v$v candidate.`r`n`r`n")}
    Reject 'current handoff cannot call closed current version a candidate' 'NEXT-AGENT-START-HERE.md' {param($x) $v=(Get-Content -Raw -Encoding UTF8 -LiteralPath (Join-Path $fixture 'VERSION.txt')).Trim(); [regex]::Replace($x,'(?ms)^## Verification boundary\s*.*?(?=^## |\z)',"## Verification boundary`r`n`r`nv$v requires fresh exact-source gates for the final v$v candidate.`r`n`r`n")}
    Reject 'current closure run must match projected state' $fixtureEvidenceRelative {param($x) $x -replace "run_id=$fixtureRun",'run_id=424243'}

    Write-Host 'PASS: MHW project-governance negative fixtures fail closed after Toolbox takeover.' -ForegroundColor Green
}finally{
    if(Test-Path -LiteralPath $fixture){Remove-Item -LiteralPath $fixture -Recurse -Force -ErrorAction SilentlyContinue}
}
