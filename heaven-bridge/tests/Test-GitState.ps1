$ErrorActionPreference = 'Stop'

$BridgeDir = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$GitStateHelper = Join-Path $BridgeDir 'git-state.ps1'
. $GitStateHelper

function Invoke-TestGit {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Repository,
        [Parameter(Mandatory = $true)]
        [string[]]$Arguments
    )

    & git -C $Repository @Arguments | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw ('git {0} failed with exit code {1}' -f ($Arguments -join ' '), $LASTEXITCODE)
    }
}

$fixture = Join-Path ([System.IO.Path]::GetTempPath()) ("heaven-bridge-git-state-" + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $fixture | Out-Null

try {
    Invoke-TestGit -Repository $fixture -Arguments @('init')
    Invoke-TestGit -Repository $fixture -Arguments @('config', 'user.email', 'heaven-bridge-test@example.invalid')
    Invoke-TestGit -Repository $fixture -Arguments @('config', 'user.name', 'Heaven Bridge Test')

    Set-Content -Path (Join-Path $fixture 'fixture.txt') -Value 'fixture' -Encoding ASCII
    Invoke-TestGit -Repository $fixture -Arguments @('add', 'fixture.txt')
    Invoke-TestGit -Repository $fixture -Arguments @('commit', '-m', 'fixture')

    $attached = Get-HeavenBridgeGitCurrentBranch -Repository $fixture
    if ([string]::IsNullOrWhiteSpace($attached)) {
        throw 'Attached Git checkout unexpectedly returned no branch name.'
    }

    Invoke-TestGit -Repository $fixture -Arguments @('checkout', '--detach', 'HEAD')
    $detached = Get-HeavenBridgeGitCurrentBranch -Repository $fixture
    if ($null -ne $detached) {
        throw "Detached Git checkout should return null branch state, got '$detached'."
    }

    Write-Output 'HEAVEN_BRIDGE_GIT_STATE_TEST_OK'
} finally {
    Remove-Item -Recurse -Force -Path $fixture -ErrorAction SilentlyContinue
}
