param([string]$Root = '', [string]$ToolboxRoot = $env:HEAVEN_TOOLBOX_ROOT)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if ([string]::IsNullOrWhiteSpace($Root)) { $Root = Join-Path $PSScriptRoot '..\..' }
$Root = (Resolve-Path -LiteralPath $Root).Path
$pin = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'toolbox-security-policy.json') -Raw | ConvertFrom-Json
if ($pin.schemaVersion -ne 1 -or $pin.repository -ne 'fengie/heaven-toolbox' -or
    $pin.revision -notmatch '^[0-9a-f]{40}$') { throw 'Invalid immutable Toolbox security pin.' }
if ([string]::IsNullOrWhiteSpace($ToolboxRoot)) {
    foreach ($candidate in @((Join-Path $HOME 'HeavenBridgeSource'), (Join-Path $HOME 'local-ai-workspaces\heaven-toolbox'))) {
        if (Test-Path -LiteralPath (Join-Path $candidate '.git')) { $ToolboxRoot = $candidate; break }
    }
}
if ([string]::IsNullOrWhiteSpace($ToolboxRoot)) { throw 'Pinned Toolbox checkout is unavailable; provision HEAVEN_TOOLBOX_ROOT.' }
$ToolboxRoot = (Resolve-Path -LiteralPath $ToolboxRoot).Path
$git = (Get-Command git -ErrorAction Stop).Source
$actual = & $git -C $ToolboxRoot rev-parse HEAD
if ($LASTEXITCODE -ne 0 -or $actual -ne $pin.revision) { throw 'Toolbox checkout does not match the reviewed immutable revision.' }
$origin = & $git -C $ToolboxRoot remote get-url origin
if ($LASTEXITCODE -ne 0 -or $origin -notmatch '^(?:https://github\.com/|git@github\.com:)fengie/heaven-toolbox(?:\.git)?$') {
    throw 'Toolbox checkout origin is not the canonical repository.'
}
$dirty = @(& $git -C $ToolboxRoot status --porcelain --untracked-files=all -- scripts/testing)
if ($LASTEXITCODE -ne 0 -or $dirty.Count -ne 0) { throw 'Toolbox security scripts differ from the pinned source.' }
foreach ($name in @('Test-RepositorySecurityPolicy.ps1','Test-TrackedSecretLeaks.ps1','Test-PowerShellSyntax.ps1')) {
    if (-not (Test-Path -LiteralPath (Join-Path $ToolboxRoot "scripts/testing/$name") -PathType Leaf)) { throw 'Canonical Toolbox security script is missing.' }
}
& (Join-Path $ToolboxRoot 'scripts/testing/Test-RepositorySecurityPolicy.ps1') -Root $Root -Consumer -ConsumerPolicyPath (Join-Path $PSScriptRoot 'mhw-security-consumer-policy.json')
& (Join-Path $ToolboxRoot 'scripts/testing/Test-PowerShellSyntax.ps1') -Root $Root
Write-Host "PASS: MHW consumes canonical Toolbox security baseline $($pin.revision)." -ForegroundColor Green
