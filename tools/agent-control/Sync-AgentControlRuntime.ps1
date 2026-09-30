param(
    [string]$RepoRoot = $env:AGENT_CONTROL_REPO
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($RepoRoot)) {
    $RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
} else {
    $RepoRoot = (Resolve-Path $RepoRoot).Path
}

$gitCommand = Get-Command git.exe -ErrorAction SilentlyContinue
if (-not $gitCommand) {
    $gitCommand = Get-Command git -ErrorAction Stop
}
$gitExe = $gitCommand.Source

function Invoke-RepoGit {
    param(
        [string[]]$Arguments,
        [switch]$AllowFailure
    )

    $output = & $gitExe -C $RepoRoot @Arguments 2>&1
    $exitCode = $LASTEXITCODE
    $text = (($output | ForEach-Object { [string]$_ }) -join [Environment]::NewLine).Trim()
    if (-not $AllowFailure -and $exitCode -ne 0) {
        throw ('git {0} failed with exit code {1}: {2}' -f ($Arguments -join ' '), $exitCode, $text)
    }
    return [pscustomobject]@{
        exit_code = $exitCode
        output = $text
    }
}

$inside = Invoke-RepoGit -Arguments @('rev-parse', '--is-inside-work-tree')
if ($inside.output -ne 'true') {
    throw "Agent Control runtime source is not a Git worktree: $RepoRoot"
}

$branch = (Invoke-RepoGit -Arguments @('branch', '--show-current')).output
if ([string]::IsNullOrWhiteSpace($branch)) {
    throw 'Agent Control runtime source is detached; refusing startup mutation.'
}
if ($branch -ne 'main') {
    throw "Agent Control runtime source must be on main; observed branch: $branch"
}

$dirty = (Invoke-RepoGit -Arguments @('status', '--porcelain', '--untracked-files=normal')).output
if (-not [string]::IsNullOrWhiteSpace($dirty)) {
    throw 'Agent Control runtime source has local changes; refusing startup mutation.'
}

$beforeSha = (Invoke-RepoGit -Arguments @('rev-parse', 'HEAD')).output
Invoke-RepoGit -Arguments @('fetch', '--quiet', 'origin', 'refs/heads/main:refs/remotes/origin/main') | Out-Null
$originMainSha = (Invoke-RepoGit -Arguments @('rev-parse', 'refs/remotes/origin/main')).output

$updated = $false
if ($beforeSha -ne $originMainSha) {
    $localAncestor = Invoke-RepoGit -Arguments @('merge-base', '--is-ancestor', $beforeSha, $originMainSha) -AllowFailure
    $remoteAncestor = Invoke-RepoGit -Arguments @('merge-base', '--is-ancestor', $originMainSha, $beforeSha) -AllowFailure

    if ($localAncestor.exit_code -eq 0 -and $remoteAncestor.exit_code -ne 0) {
        Invoke-RepoGit -Arguments @('merge', '--ff-only', 'refs/remotes/origin/main') | Out-Null
        $updated = $true
    } elseif ($remoteAncestor.exit_code -eq 0 -and $localAncestor.exit_code -ne 0) {
        throw 'Agent Control local main is ahead of origin/main; refusing to launch non-canonical source.'
    } else {
        throw 'Agent Control local main diverges from origin/main; refusing startup mutation.'
    }
}

$afterSha = (Invoke-RepoGit -Arguments @('rev-parse', 'HEAD')).output
if ($afterSha -ne $originMainSha) {
    throw "Agent Control runtime source did not converge to origin/main. local=$afterSha origin=$originMainSha"
}

$packagePath = Join-Path $RepoRoot 'tools\agent-control\package.json'
if (-not (Test-Path -LiteralPath $packagePath)) {
    throw "Agent Control package metadata missing: $packagePath"
}
$package = Get-Content -LiteralPath $packagePath -Raw -Encoding UTF8 | ConvertFrom-Json
$version = [string]$package.version
if ([string]::IsNullOrWhiteSpace($version)) {
    throw "Agent Control package version is missing: $packagePath"
}

[pscustomobject]@{
    repo = $RepoRoot
    branch = $branch
    before_sha = $beforeSha
    source_sha = $afterSha
    origin_main_sha = $originMainSha
    agent_control_version = $version
    updated = $updated
}
