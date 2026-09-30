param(
    [string]$RepoRoot = $env:AGENT_CONTROL_REPO,
    [switch]$ForceReplaceOwned,
    [switch]$AllowNonControllerHost,
    [int]$Port = 7331
)

$ErrorActionPreference = 'Stop'

if (-not $AllowNonControllerHost -and $env:COMPUTERNAME -and $env:COMPUTERNAME -ine 'heaven2') {
    throw "Verified Agent Control startup must run on heaven2. Current host: $env:COMPUTERNAME"
}

if ([string]::IsNullOrWhiteSpace($RepoRoot)) {
    $RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
} else {
    $RepoRoot = (Resolve-Path -LiteralPath $RepoRoot).Path
}

$policyPath = Join-Path $RepoRoot 'tools\agent-control\Startup-Policy.ps1'
if (-not (Test-Path -LiteralPath $policyPath)) {
    throw "Agent Control startup policy missing: $policyPath"
}
. $policyPath

$source = Get-AgentControlRuntimeSourceDisposition -RepoRoot $RepoRoot
if (-not $source.launch_safe) {
    throw ("Agent Control launch blocked by runtime-source policy ({0}): {1}" -f $source.disposition, $source.reason)
}

$agentDir = Join-Path $RepoRoot 'tools\agent-control'
$serverPath = Join-Path $agentDir 'server.mjs'
$packagePath = Join-Path $agentDir 'package.json'
$productVersionPath = Join-Path $RepoRoot 'VERSION.txt'
if (-not (Test-Path -LiteralPath $serverPath)) { throw "Agent Control server missing: $serverPath" }
if (-not (Test-Path -LiteralPath $packagePath)) { throw "Agent Control package metadata missing: $packagePath" }
if (-not (Test-Path -LiteralPath $productVersionPath)) { throw "Product version metadata missing: $productVersionPath" }

$package = Get-Content -LiteralPath $packagePath -Raw -Encoding UTF8 | ConvertFrom-Json
$agentControlVersion = [string]$package.version
$productVersion = (Get-Content -LiteralPath $productVersionPath -Raw -Encoding UTF8).Trim()
if ([string]::IsNullOrWhiteSpace($agentControlVersion) -or [string]::IsNullOrWhiteSpace($productVersion)) {
    throw 'Agent Control or product version metadata is empty.'
}

$dataDir = if ([string]::IsNullOrWhiteSpace($env:AGENT_CONTROL_DATA_DIR)) { Join-Path $agentDir 'data' } else { $env:AGENT_CONTROL_DATA_DIR }
$controllerPidPath = Join-Path $dataDir 'controller-process.json'
$healthUri = "http://127.0.0.1:$Port/api/status"

function Get-AgentControlListenerPid {
    $cmd = Get-Command Get-NetTCPConnection -ErrorAction SilentlyContinue
    if (-not $cmd) {
        throw 'Get-NetTCPConnection is required to prove Agent Control listener ownership.'
    }
    $listener = Get-NetTCPConnection -LocalAddress '127.0.0.1' -LocalPort $Port -State Listen -ErrorAction SilentlyContinue |
        Select-Object -First 1
    if ($listener -and $listener.OwningProcess) { return [int]$listener.OwningProcess }
    return $null
}

function Read-AgentControlIdentity {
    if (-not (Test-Path -LiteralPath $controllerPidPath)) { return $null }
    try {
        return Get-Content -LiteralPath $controllerPidPath -Raw -Encoding UTF8 | ConvertFrom-Json
    } catch {
        return $null
    }
}

function Get-AgentControlObservedProcess {
    param([int]$ProcessId)
    if (-not $ProcessId) { return $null }
    try {
        return Get-CimInstance Win32_Process -Filter "ProcessId = $ProcessId" -ErrorAction Stop
    } catch {
        return $null
    }
}

function Test-AgentControlIdentityMatchesVerifiedSource {
    param($Identity)
    if ($null -eq $Identity) { return $false }
    $expectedServer = ConvertTo-AgentControlCanonicalPath -Path $serverPath
    $actualServer = ConvertTo-AgentControlCanonicalPath -Path ([string]$Identity.serverPath)
    return (
        $expectedServer -and
        $actualServer -eq $expectedServer -and
        [string]$Identity.sourceBranch -eq 'main' -and
        [string]$Identity.sourceSha -eq [string]$source.after_sha -and
        [string]$Identity.remoteMainSha -eq [string]$source.remote_sha -and
        [string]$Identity.agentControlVersion -eq $agentControlVersion -and
        [string]$Identity.productVersion -eq $productVersion
    )
}

$listenerPid = Get-AgentControlListenerPid
$replaced = $false
$alreadyRunning = $false

if ($listenerPid) {
    $identity = Read-AgentControlIdentity
    $observed = Get-AgentControlObservedProcess -ProcessId $listenerPid
    $ownership = Test-AgentControlProcessOwnership -ListenerPid $listenerPid -Port $Port -Identity $identity -Process $observed -ExpectedServerPath $serverPath
    if (-not $ownership.owned) {
        throw ("Port {0} is occupied by PID {1}, but Agent Control ownership is not proven: {2}" -f $Port, $listenerPid, $ownership.reason)
    }

    $identityCurrent = Test-AgentControlIdentityMatchesVerifiedSource -Identity $identity
    if ($identityCurrent -and -not $ForceReplaceOwned) {
        try {
            $status = Invoke-RestMethod -Uri $healthUri -Method Get -TimeoutSec 3
        } catch {
            throw ("A current-source Agent Control listener exists but its health endpoint failed: {0}" -f $_.Exception.Message)
        }
        if (-not [bool]$status.ok) { throw 'A current-source Agent Control listener returned unhealthy status.' }
        if ([string]$status.controller.sourceSha -ne [string]$source.after_sha -or [string]$status.controller.sourceBranch -ne 'main') {
            throw 'Agent Control health identity does not match its persisted verified source.'
        }
        $alreadyRunning = $true
        return [pscustomobject]@{
            ok = $true
            started = $false
            replaced = $false
            already_running = $true
            source = $source
            identity = $identity
            status = $status
        }
    }

    Stop-Process -Id $listenerPid -Force -ErrorAction Stop
    $replaced = $true
    for ($i = 0; $i -lt 40; $i++) {
        Start-Sleep -Milliseconds 125
        if (-not (Get-AgentControlListenerPid)) { break }
    }
    if (Get-AgentControlListenerPid) {
        throw "Proven-owned Agent Control PID $listenerPid did not release port $Port."
    }
    $staleIdentity = Read-AgentControlIdentity
    if ($staleIdentity -and [int]$staleIdentity.pid -eq $listenerPid) {
        Remove-Item -LiteralPath $controllerPidPath -Force -ErrorAction SilentlyContinue
    }
}

$node = Get-Command node.exe -ErrorAction SilentlyContinue
if (-not $node) { $node = Get-Command node -ErrorAction Stop }

$env:AGENT_CONTROL_REPO = $RepoRoot
$env:AGENT_CONTROL_SKIP_LOCAL_BRIDGE_BOOTSTRAP = '1'
$env:AGENT_CONTROL_SOURCE_SHA = [string]$source.after_sha
$env:AGENT_CONTROL_SOURCE_BRANCH = 'main'
$env:AGENT_CONTROL_SOURCE_REMOTE_SHA = [string]$source.remote_sha
$env:AGENT_CONTROL_SOURCE_DISPOSITION = [string]$source.disposition
$env:AGENT_CONTROL_EXPECTED_VERSION = $agentControlVersion
$env:AGENT_CONTROL_EXPECTED_PRODUCT_VERSION = $productVersion

$child = Start-Process -FilePath $node.Source -ArgumentList @('server.mjs') -WorkingDirectory $agentDir -WindowStyle Hidden -PassThru

$verifiedIdentity = $null
$status = $null
$deadline = (Get-Date).ToUniversalTime().AddSeconds(15)
while ((Get-Date).ToUniversalTime() -lt $deadline) {
    Start-Sleep -Milliseconds 250
    if ($child.HasExited) {
        throw "Agent Control exited during verified startup with code $($child.ExitCode)."
    }

    $currentListener = Get-AgentControlListenerPid
    if (-not $currentListener -or [int]$currentListener -ne [int]$child.Id) { continue }

    $candidateIdentity = Read-AgentControlIdentity
    if (-not $candidateIdentity -or [int]$candidateIdentity.pid -ne [int]$child.Id) { continue }
    if (-not (Test-AgentControlIdentityMatchesVerifiedSource -Identity $candidateIdentity)) { continue }

    try {
        $candidateStatus = Invoke-RestMethod -Uri $healthUri -Method Get -TimeoutSec 3
    } catch {
        continue
    }
    if (-not [bool]$candidateStatus.ok) { continue }
    if (
        [string]$candidateStatus.controller.sourceBranch -ne 'main' -or
        [string]$candidateStatus.controller.sourceSha -ne [string]$source.after_sha -or
        [string]$candidateStatus.controller.agentControlVersion -ne $agentControlVersion -or
        [string]$candidateStatus.controller.productVersion -ne $productVersion
    ) {
        continue
    }

    $verifiedIdentity = $candidateIdentity
    $status = $candidateStatus
    break
}

if (-not $verifiedIdentity) {
    try {
        if (-not $child.HasExited) { Stop-Process -Id $child.Id -Force -ErrorAction SilentlyContinue }
    } catch {}
    throw 'Agent Control started but exact process-start source identity could not be verified.'
}

[pscustomobject]@{
    ok = $true
    started = $true
    replaced = $replaced
    already_running = $alreadyRunning
    source = $source
    identity = $verifiedIdentity
    status = $status
}
