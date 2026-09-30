param(
    [string]$RepoRoot = $env:AGENT_CONTROL_REPO,
    [string]$ExpectedSourceSha = $env:AGENT_CONTROL_SOURCE_SHA,
    [string]$ExpectedAgentControlVersion = $env:AGENT_CONTROL_VERSION,
    [int]$CheckIntervalSeconds = 10,
    [int]$UnhealthyChecksBeforeRestart = 3,
    [int]$MaxRestartsPerWindow = 6,
    [int]$RestartWindowMinutes = 10,
    [int]$BaseCooldownSeconds = 60,
    [int]$MaxCooldownSeconds = 900,
    [switch]$AllowNonControllerHost
)

$ErrorActionPreference = 'Stop'

if (-not $AllowNonControllerHost -and $env:COMPUTERNAME -and $env:COMPUTERNAME -ine 'heaven2') {
    throw "Agent Control watchdog must run on heaven2. Current host: $env:COMPUTERNAME"
}

if ([string]::IsNullOrWhiteSpace($RepoRoot)) {
    $RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
} else {
    $RepoRoot = (Resolve-Path $RepoRoot).Path
}

$CheckIntervalSeconds = [Math]::Max(2, $CheckIntervalSeconds)
$UnhealthyChecksBeforeRestart = [Math]::Max(1, $UnhealthyChecksBeforeRestart)
$MaxRestartsPerWindow = [Math]::Max(1, $MaxRestartsPerWindow)
$RestartWindowMinutes = [Math]::Max(1, $RestartWindowMinutes)
$BaseCooldownSeconds = [Math]::Max(60, $BaseCooldownSeconds)
$MaxCooldownSeconds = [Math]::Max($BaseCooldownSeconds, $MaxCooldownSeconds)

$appDir = Join-Path $env:LOCALAPPDATA 'MHW-Agent-Control'
New-Item -ItemType Directory -Force -Path $appDir | Out-Null

$logPath = Join-Path $appDir 'agent-control-watchdog.log'
$statePath = Join-Path $appDir 'agent-control-watchdog-state.json'
$lockPath = Join-Path $appDir 'agent-control-watchdog.lock'
$agentDir = Join-Path $RepoRoot 'tools\agent-control'
$serverPath = Join-Path $agentDir 'server.mjs'
$dataDir = if ([string]::IsNullOrWhiteSpace($env:AGENT_CONTROL_DATA_DIR)) { Join-Path $agentDir 'data' } else { $env:AGENT_CONTROL_DATA_DIR }
$controllerPidPath = Join-Path $dataDir 'controller-process.json'
$syncScript = Join-Path $agentDir 'Sync-AgentControlRuntime.ps1'
$healthUri = 'http://127.0.0.1:7331/api/status'
$port = 7331

function Write-WatchdogLog {
    param([string]$Message)
    $line = '{0} {1}' -f (Get-Date).ToUniversalTime().ToString('o'), $Message
    Add-Content -LiteralPath $logPath -Value $line -Encoding UTF8
}

function Read-WatchdogState {
    if (-not (Test-Path -LiteralPath $statePath)) {
        return [pscustomobject]@{
            restart_timestamps_utc = @()
            cooldown_level = 0
            last_start_utc = $null
            last_healthy_utc = $null
        }
    }

    try {
        $loaded = Get-Content -LiteralPath $statePath -Raw -Encoding UTF8 | ConvertFrom-Json
        if ($null -eq $loaded.restart_timestamps_utc) {
            $loaded | Add-Member -NotePropertyName restart_timestamps_utc -NotePropertyValue @()
        }
        if ($null -eq $loaded.cooldown_level) {
            $loaded | Add-Member -NotePropertyName cooldown_level -NotePropertyValue 0
        }
        return $loaded
    } catch {
        $backup = "$statePath.corrupt-$(Get-Date -Format 'yyyyMMdd-HHmmss')"
        Copy-Item -LiteralPath $statePath -Destination $backup -Force -ErrorAction SilentlyContinue
        Write-WatchdogLog ("Recovered unreadable watchdog state; backup={0}" -f $backup)
        return [pscustomobject]@{
            restart_timestamps_utc = @()
            cooldown_level = 0
            last_start_utc = $null
            last_healthy_utc = $null
        }
    }
}

function Write-WatchdogState {
    param($State)
    $State | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $statePath -Encoding UTF8
}

function Test-AgentControlHealth {
    try {
        $status = Invoke-RestMethod -Uri $healthUri -Method Get -TimeoutSec 3
        return [bool]$status.ok
    } catch {
        return $false
    }
}

function Get-AgentControlListenerPid {
    try {
        if (Get-Command Get-NetTCPConnection -ErrorAction SilentlyContinue) {
            $listener = Get-NetTCPConnection -LocalAddress '127.0.0.1' -LocalPort $port -State Listen -ErrorAction SilentlyContinue |
                Select-Object -First 1
            if ($listener -and $listener.OwningProcess) {
                return [int]$listener.OwningProcess
            }
        }
    } catch {}
    return $null
}

function Test-IsOwnedAgentControlProcess {
    param([int]$ProcessId)
    if (-not $ProcessId) { return $false }

    try {
        if (-not (Test-Path -LiteralPath $controllerPidPath)) { return $false }
        $identity = Get-Content -LiteralPath $controllerPidPath -Raw -Encoding UTF8 | ConvertFrom-Json
        if ([int]$identity.pid -ne $ProcessId) { return $false }
        if ($identity.port -and [int]$identity.port -ne $port) { return $false }

        $process = Get-CimInstance Win32_Process -Filter "ProcessId = $ProcessId" -ErrorAction Stop
        $command = [string]$process.CommandLine
        if ([string]::IsNullOrWhiteSpace($command)) { return $false }

        $serverIdentity = [string]$identity.serverPath
        if ([string]::IsNullOrWhiteSpace($serverIdentity)) { return $false }
        $identityServer = [System.IO.Path]::GetFullPath($serverIdentity)
        $expectedServer = [System.IO.Path]::GetFullPath($serverPath)
        if ($identityServer -ine $expectedServer) { return $false }

        return $command -match '(?i)node(?:\.exe)?' -and $command -match '(?i)server\.mjs'
    } catch {
        return $false
    }
}

function Stop-HungOwnedAgentControl {
    $listenerPid = Get-AgentControlListenerPid
    if (-not $listenerPid) { return $true }

    if (-not (Test-IsOwnedAgentControlProcess -ProcessId $listenerPid)) {
        Write-WatchdogLog ("Port {0} is occupied by PID {1} but ownership is not proven; refusing to kill it." -f $port, $listenerPid)
        return $false
    }

    try {
        Stop-Process -Id $listenerPid -Force -ErrorAction Stop
        Write-WatchdogLog ("Stopped unhealthy proven-owned Agent Control PID {0}." -f $listenerPid)
        Start-Sleep -Seconds 1
        return $true
    } catch {
        Write-WatchdogLog ("Failed to stop unhealthy Agent Control PID {0}: {1}" -f $listenerPid, $_.Exception.Message)
        return $false
    }
}

function Sync-AgentControlSource {
    if (-not (Test-Path -LiteralPath $syncScript)) {
        throw "Agent Control runtime freshness guard missing: $syncScript"
    }
    $runtime = & $syncScript -RepoRoot $RepoRoot
    $script:ExpectedSourceSha = [string]$runtime.source_sha
    $script:ExpectedAgentControlVersion = [string]$runtime.agent_control_version
    $env:AGENT_CONTROL_SOURCE_SHA = $script:ExpectedSourceSha
    $env:AGENT_CONTROL_VERSION = $script:ExpectedAgentControlVersion
    return $runtime
}

function Start-AgentControlServer {
    $runtime = Sync-AgentControlSource
    if (-not (Test-Path -LiteralPath $serverPath)) {
        throw "Agent Control server missing: $serverPath"
    }

    $node = (Get-Command node.exe -ErrorAction Stop).Source
    $env:AGENT_CONTROL_REPO = $RepoRoot
    $env:AGENT_CONTROL_SKIP_LOCAL_BRIDGE_BOOTSTRAP = '1'
    Start-Process -FilePath $node -ArgumentList @('server.mjs') -WorkingDirectory $agentDir -WindowStyle Hidden
    Write-WatchdogLog ("Started Agent Control server from {0} at source {1} / v{2}." -f $agentDir, $runtime.source_sha, $runtime.agent_control_version)
}

try {
    $lockStream = [System.IO.File]::Open(
        $lockPath,
        [System.IO.FileMode]::OpenOrCreate,
        [System.IO.FileAccess]::ReadWrite,
        [System.IO.FileShare]::None
    )
} catch {
    exit 0
}

try {
    if ([string]::IsNullOrWhiteSpace($ExpectedSourceSha) -or [string]::IsNullOrWhiteSpace($ExpectedAgentControlVersion)) {
        $runtime = Sync-AgentControlSource
        Write-WatchdogLog ("Watchdog canonicalized runtime source to {0} / v{1}." -f $runtime.source_sha, $runtime.agent_control_version)
    } else {
        $env:AGENT_CONTROL_SOURCE_SHA = $ExpectedSourceSha
        $env:AGENT_CONTROL_VERSION = $ExpectedAgentControlVersion
    }
    Write-WatchdogLog ("Watchdog started; repo={0} interval={1}s" -f $RepoRoot, $CheckIntervalSeconds)
    $unhealthyCount = 0

    while ($true) {
        if (Test-AgentControlHealth) {
            $unhealthyCount = 0
            $state = Read-WatchdogState
            $state.last_healthy_utc = (Get-Date).ToUniversalTime().ToString('o')
            $windowStart = (Get-Date).ToUniversalTime().AddMinutes(-$RestartWindowMinutes)
            $recent = @($state.restart_timestamps_utc | Where-Object {
                try { [DateTime]::Parse([string]$_).ToUniversalTime() -ge $windowStart } catch { $false }
            })
            $state.restart_timestamps_utc = $recent
            if ($recent.Count -eq 0) { $state.cooldown_level = 0 }
            Write-WatchdogState $state
            Start-Sleep -Seconds $CheckIntervalSeconds
            continue
        }

        $unhealthyCount++
        if ($unhealthyCount -lt $UnhealthyChecksBeforeRestart) {
            Start-Sleep -Seconds $CheckIntervalSeconds
            continue
        }
        $unhealthyCount = 0

        $state = Read-WatchdogState
        $now = (Get-Date).ToUniversalTime()
        $windowStart = $now.AddMinutes(-$RestartWindowMinutes)
        $recent = @($state.restart_timestamps_utc | Where-Object {
            try { [DateTime]::Parse([string]$_).ToUniversalTime() -ge $windowStart } catch { $false }
        })

        if ($recent.Count -ge $MaxRestartsPerWindow) {
            $level = [Math]::Max(0, [int]$state.cooldown_level)
            $cooldownSeconds = [Math]::Min($MaxCooldownSeconds, $BaseCooldownSeconds * [Math]::Pow(2, [Math]::Min(8, $level)))
            $state.cooldown_level = [Math]::Min(12, $level + 1)
            $state.restart_timestamps_utc = $recent
            Write-WatchdogState $state
            Write-WatchdogLog ("Restart intensity limit reached ({0}/{1} in {2}m); cooling down for {3}s." -f $recent.Count, $MaxRestartsPerWindow, $RestartWindowMinutes, $cooldownSeconds)
            Start-Sleep -Seconds ([int]$cooldownSeconds)
            continue
        }

        if (-not (Stop-HungOwnedAgentControl)) {
            Start-Sleep -Seconds $CheckIntervalSeconds
            continue
        }

        try {
            Start-AgentControlServer
            $startedAt = (Get-Date).ToUniversalTime().ToString('o')
            $recent += $startedAt
            $state.restart_timestamps_utc = @($recent)
            $state.last_start_utc = $startedAt
            Write-WatchdogState $state
        } catch {
            Write-WatchdogLog ("Agent Control restart attempt failed: {0}" -f $_.Exception.Message)
        }

        Start-Sleep -Seconds $CheckIntervalSeconds
    }
} finally {
    if ($lockStream) {
        $lockStream.Dispose()
    }
}
