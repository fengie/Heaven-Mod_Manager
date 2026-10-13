param([string]$ToolboxRoot)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$fixture = Join-Path ([IO.Path]::GetTempPath()) ('mhw-security-adapter-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $fixture | Out-Null
try {
    $adapter = Join-Path $fixture 'Test-ToolboxSecurityBaseline.ps1'
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Test-ToolboxSecurityBaseline.ps1') -Destination $adapter
    $pinPath = Join-Path $fixture 'toolbox-security-policy.json'
    $root = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
    function Assert-Rejected([string]$Expected, [string]$Checkout) {
        $rejected = $false
        try { & $adapter -Root $root -ToolboxRoot $Checkout }
        catch { if ($_.Exception.Message -notlike "*$Expected*") { throw }; $rejected = $true }
        if (-not $rejected) { throw "Expected rejection: $Expected" }
    }
    '{"schemaVersion":1,"repository":"fengie/heaven-toolbox","revision":"main"}' | Set-Content -LiteralPath $pinPath
    Assert-Rejected 'Invalid immutable Toolbox security pin' $fixture
    '{"schemaVersion":1,"repository":"fengie/heaven-toolbox","revision":"0000000000000000000000000000000000000000"}' | Set-Content -LiteralPath $pinPath
    Assert-Rejected 'does not match the reviewed immutable revision' $root
    Assert-Rejected 'does not exist' (Join-Path $fixture 'missing')
    if (-not [string]::IsNullOrWhiteSpace($ToolboxRoot)) {
        $revision = (& git -C $ToolboxRoot rev-parse HEAD).Trim()
        if ($LASTEXITCODE -ne 0) { throw 'Unable to inspect fixture Toolbox revision.' }
        @{schemaVersion=1;repository='fengie/heaven-toolbox';revision=$revision} | ConvertTo-Json | Set-Content -LiteralPath $pinPath
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'mhw-security-consumer-policy.json') -Destination $fixture
        & $adapter -Root $root -ToolboxRoot $ToolboxRoot
        Write-Host 'PASS: pinned canonical consumer execution.'
    }
    Write-Host 'PASS: adapter rejects mutable pins, mismatched revisions, and unavailable checkouts.'
} finally {
    # This fixture is a fresh UUID directory created above, never a caller-supplied path.
    Remove-Item -LiteralPath $fixture -Recurse -Force
}
