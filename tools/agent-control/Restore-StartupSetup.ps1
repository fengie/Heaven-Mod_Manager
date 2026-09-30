param(
    [string]$RepoRoot = $env:AGENT_CONTROL_REPO,
    [string]$ExpectedSourceSha = $env:AGENT_CONTROL_SOURCE_SHA,
    [string]$ExpectedAgentControlVersion = $env:AGENT_CONTROL_VERSION,
    [switch]$AllowNonControllerHost
)

$ErrorActionPreference = 'Stop'

if (-not $AllowNonControllerHost -and $env:COMPUTERNAME -and $env:COMPUTERNAME -ine 'heaven2') {
    throw "Startup setup restore must run on heaven2. Current host: $env:COMPUTERNAME"
}

if ([string]::IsNullOrWhiteSpace($RepoRoot)) {
    $RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
} else {
    $RepoRoot = (Resolve-Path $RepoRoot).Path
}

$appDir = Join-Path $env:LOCALAPPDATA 'MHW-Agent-Control'
New-Item -ItemType Directory -Force -Path $appDir | Out-Null

$logPath = Join-Path $appDir 'startup-restore.log'
$statusPath = Join-Path $appDir 'startup-last-status.json'
$profilePath = Join-Path $appDir 'startup-profile.json'
$lockPath = Join-Path $appDir 'startup-restore.lock'
$agentDir = Join-Path $RepoRoot 'tools\agent-control'
$serverPath = Join-Path $agentDir 'server.mjs'
$syncScript = Join-Path $agentDir 'Sync-AgentControlRuntime.ps1'
$dataDir = if ([string]::IsNullOrWhiteSpace($env:AGENT_CONTROL_DATA_DIR)) { Join-Path $agentDir 'data' } else { $env:AGENT_CONTROL_DATA_DIR }
$controllerPidPath = Join-Path $dataDir 'controller-process.json'

function Write-RestoreLog {
    param([string]$Message)
    $line = '{0} {1}' -f (Get-Date).ToUniversalTime().ToString('o'), $Message
    Add-Content -LiteralPath $logPath -Value $line -Encoding UTF8
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
    $defaultProfile = [ordered]@{
        version = 1
        enabled = $true
        heaven_bridge = $true
        agent_control = $true
        plugin_workspace = $true
        open_dashboard = $false
    }

    if (-not (Test-Path -LiteralPath $profilePath)) {
        $defaultProfile | ConvertTo-Json | Set-Content -LiteralPath $profilePath -Encoding UTF8
    }

    try {
        $profile = Get-Content -LiteralPath $profilePath -Raw -Encoding UTF8 | ConvertFrom-Json
    } catch {
        $backup = "$profilePath.corrupt-$(Get-Date -Format 'yyyyMMdd-HHmmss')"
        Copy-Item -LiteralPath $profilePath -Destination $backup -Force -ErrorAction SilentlyContinue
        $defaultProfile | ConvertTo-Json | Set-Content -LiteralPath $profilePath -Encoding UTF8
        $profile = [pscustomobject]$defaultProfile
        Write-RestoreLog "Recovered an unreadable startup profile; backup=$backup"
    }

    $profileEnabled = if ($null -eq $profile.enabled) { $true } else { [bool]$profile.enabled }
    if (-not $profileEnabled) {
        Write-RestoreLog 'Startup restore is disabled by startup-profile.json.'
        exit 0
    }

    $result = [ordered]@{
        timestamp_utc = (Get-Date).ToUniversalTime().ToString('o')
        repo = $RepoRoot
        heaven_bridge = [ordered]@{
            requested = $false
            scheduled_tasks_found = 0
            scheduled_tasks_started = 0
            startup_fallback_present = $false
            warning = $null
        }
        agent_control = [ordered]@{
            requested = $false
            watchdog_task_found = $false
            watchdog_task_started = $false
            watchdog_fallback_present = $false
            already_listening = $false
            started = $false
            healthy = $false
            expected_source_sha = $null
            expected_version = $null
            replaced_stale_controller = $false
            refused_unowned_listener = $false
            error = $null
        }
        plugin_workspace = [ordered]@{
            requested = $false
            manifest_count = 0
            plugins = @()
            warning = $null
        }
    }

    $pluginWorkspaceEnabled = if ($null -eq $profile.plugin_workspace) { $true } else { [bool]$profile.plugin_workspace }
    if ($pluginWorkspaceEnabled) {
        $result.plugin_workspace.requested = $true
        $pluginRoot = Join-Path $RepoRoot 'plugins'
        if (Test-Path -LiteralPath $pluginRoot) {
            $plugins = @()
            foreach ($dir in Get-ChildItem -LiteralPath $pluginRoot -Directory -ErrorAction SilentlyContinue) {
                $manifestPath = Join-Path $dir.FullName 'manifest.json'
                if (-not (Test-Path -LiteralPath $manifestPath)) { continue }
                try {
                    $manifest = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
                    $plugins += [pscustomobject]@{
                        name = if ($manifest.name) { [string]$manifest.name } else { $dir.Name }
                        version = if ($manifest.version) { [string]$manifest.version } else { $null }
                        path = $manifestPath
                        ready = $true
                    }
                } catch {
                    $plugins += [pscustomobject]@{
                        name = $dir.Name
                        version = $null
                        path = $manifestPath
                        ready = $false
                    }
                }
            }
            $result.plugin_workspace.plugins = @($plugins | Sort-Object name)
            $result.plugin_workspace.manifest_count = $plugins.Count
            Write-RestoreLog "Plugin workspace discovered $($plugins.Count) manifest-backed local plugins."
        } else {
            $result.plugin_workspace.warning = "Plugin workspace missing: $pluginRoot"
            Write-RestoreLog $result.plugin_workspace.warning
        }
    }

    $bridgeEnabled = if ($null -eq $profile.heaven_bridge) { $true } else { [bool]$profile.heaven_bridge }
    if ($bridgeEnabled) {
        $result.heaven_bridge.requested = $true
        $bridgeTaskNames = @('Heaven Local Bridge', 'Heaven Local Bridge Watchdog')
        $scheduledTaskCmd = Get-Command Get-ScheduledTask -ErrorAction SilentlyContinue
        if ($scheduledTaskCmd) {
            foreach ($taskName in $bridgeTaskNames) {
                try {
                    $task = Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue
                    if (-not $task) { continue }
                    $result.heaven_bridge.scheduled_tasks_found++
                    if ([string]$task.State -ne 'Running') {
                        Start-ScheduledTask -TaskName $taskName -ErrorAction Stop
                        $result.heaven_bridge.scheduled_tasks_started++
                        Write-RestoreLog "Started scheduled task: $taskName"
                    }
                } catch {
                    Write-RestoreLog "Could not start bridge task ${taskName}: $($_.Exception.Message)"
                }
            }
        }

        $startupDir = [Environment]::GetFolderPath('Startup')
        $bridgeFallback = if ([string]::IsNullOrWhiteSpace($startupDir)) { $null } else { Join-Path $startupDir 'HeavenBridgeWatchdog.vbs' }
        $result.heaven_bridge.startup_fallback_present = [bool]($bridgeFallback -and (Test-Path -LiteralPath $bridgeFallback))

        if ($result.heaven_bridge.scheduled_tasks_found -eq 0 -and -not $result.heaven_bridge.startup_fallback_present) {
            $result.heaven_bridge.warning = 'Heaven Bridge persistence was not found. Run heaven-bridge\bootstrap.ps1 once to repair its registered startup infrastructure.'
            Write-RestoreLog $result.heaven_bridge.warning
        }
    }

    function Test-LocalTcpPort {
        param(
            [int]$Port,
            [int]$TimeoutMs = 500
        )
        $client = New-Object System.Net.Sockets.TcpClient
        try {
            $async = $client.BeginConnect('127.0.0.1', $Port, $null, $null)
            if (-not $async.AsyncWaitHandle.WaitOne($TimeoutMs)) { return $false }
            $client.EndConnect($async)
            return $true
        } catch {
            return $false
        } finally {
            $client.Dispose()
        }
    }

    function Get-AgentControlListenerPid {
        param([int]$Port)
        try {
            if (Get-Command Get-NetTCPConnection -ErrorAction SilentlyContinue) {
                $listener = Get-NetTCPConnection -LocalAddress '127.0.0.1' -LocalPort $Port -State Listen -ErrorAction SilentlyContinue |
                    Select-Object -First 1
                if ($listener -and $listener.OwningProcess) { return [int]$listener.OwningProcess }
            }
        } catch {}
        return $null
    }

    function Test-IsOwnedAgentControlProcess {
        param(
            [int]$ProcessId,
            [int]$Port
        )
        if (-not $ProcessId -or -not (Test-Path -LiteralPath $controllerPidPath)) { return $false }
        try {
            $identity = Get-Content -LiteralPath $controllerPidPath -Raw -Encoding UTF8 | ConvertFrom-Json
            if ([int]$identity.pid -ne $ProcessId) { return $false }
            if ($identity.port -and [int]$identity.port -ne $Port) { return $false }
            if ([string]::IsNullOrWhiteSpace([string]$identity.serverPath)) { return $false }

            $identityServer = [System.IO.Path]::GetFullPath([string]$identity.serverPath)
            $expectedServer = [System.IO.Path]::GetFullPath($serverPath)
            if ($identityServer -ine $expectedServer) { return $false }

            $process = Get-CimInstance Win32_Process -Filter "ProcessId = $ProcessId" -ErrorAction Stop
            $command = [string]$process.CommandLine
            return -not [string]::IsNullOrWhiteSpace($command) -and
                $command -match '(?i)node(?:\.exe)?' -and
                $command -match '(?i)server\.mjs'
        } catch {
            return $false
        }
    }

    if ([string]::IsNullOrWhiteSpace($ExpectedSourceSha) -or [string]::IsNullOrWhiteSpace($ExpectedAgentControlVersion)) {
        if (-not (Test-Path -LiteralPath $syncScript)) {
            throw "Agent Control runtime freshness guard missing: $syncScript"
        }
        $runtime = & $syncScript -RepoRoot $RepoRoot
        $ExpectedSourceSha = [string]$runtime.source_sha
        $ExpectedAgentControlVersion = [string]$runtime.agent_control_version
    }
    $env:AGENT_CONTROL_SOURCE_SHA = $ExpectedSourceSha
    $env:AGENT_CONTROL_VERSION = $ExpectedAgentControlVersion

    $agentControlEnabled = if ($null -eq $profile.agent_control) { $true } else { [bool]$profile.agent_control }
    if ($agentControlEnabled) {
        $result.agent_control.requested = $true

        $agentWatchdogTaskName = 'Heaven Agent Control Watchdog'
        if (Get-Command Get-ScheduledTask -ErrorAction SilentlyContinue) {
            try {
                $agentWatchdogTask = Get-ScheduledTask -TaskName $agentWatchdogTaskName -ErrorAction SilentlyContinue
                if ($agentWatchdogTask) {
                    $result.agent_control.watchdog_task_found = $true
                    if ([string]$agentWatchdogTask.State -ne 'Running') {
                        Start-ScheduledTask -TaskName $agentWatchdogTaskName -ErrorAction Stop
                        $result.agent_control.watchdog_task_started = $true
                        Write-RestoreLog ("Started scheduled task: {0}" -f $agentWatchdogTaskName)
                    }
                }
            } catch {
                Write-RestoreLog ("Could not start Agent Control watchdog task {0}: {1}" -f $agentWatchdogTaskName, $_.Exception.Message)
            }
        }

        $startupDir = [Environment]::GetFolderPath('Startup')
        $agentWatchdogFallback = if ([string]::IsNullOrWhiteSpace($startupDir)) { $null } else { Join-Path $startupDir 'HeavenAgentControlWatchdog.vbs' }
        $result.agent_control.watchdog_fallback_present = [bool]($agentWatchdogFallback -and (Test-Path -LiteralPath $agentWatchdogFallback))
        if (-not $result.agent_control.watchdog_task_found -and -not $result.agent_control.watchdog_fallback_present) {
            Write-RestoreLog 'Agent Control watchdog persistence was not found; direct startup will restore the server, and Install-StartupRestore.ps1 should be rerun to install continuous supervision.'
        }

        $port = 7331
        $result.agent_control.expected_source_sha = $ExpectedSourceSha
        $result.agent_control.expected_version = $ExpectedAgentControlVersion
        $portOpen = Test-LocalTcpPort -Port $port
        $result.agent_control.already_listening = $portOpen

        if ($portOpen) {
            $runtimeMatches = $false
            try {
                $status = Invoke-RestMethod -Uri 'http://127.0.0.1:7331/api/status' -Method Get -TimeoutSec 3
                $runtimeMatches =
                    [bool]$status.ok -and
                    ([string]$status.controller.sourceSha -eq $ExpectedSourceSha) -and
                    ([string]$status.controller.agentControlVersion -eq $ExpectedAgentControlVersion)
            } catch {}

            if (-not $runtimeMatches) {
                $listenerPid = Get-AgentControlListenerPid -Port $port
                if (-not $listenerPid -or -not (Test-IsOwnedAgentControlProcess -ProcessId $listenerPid -Port $port)) {
                    $result.agent_control.refused_unowned_listener = $true
                    $result.agent_control.error = "Port $port is occupied by a stale or unknown listener whose Agent Control ownership could not be proven; refusing replacement."
                    Write-RestoreLog $result.agent_control.error
                } else {
                    Stop-Process -Id $listenerPid -Force -ErrorAction Stop
                    $result.agent_control.replaced_stale_controller = $true
                    Write-RestoreLog ("Stopped stale proven-owned Agent Control PID {0} before canonical restart." -f $listenerPid)
                    for ($i = 0; $i -lt 20; $i++) {
                        Start-Sleep -Milliseconds 100
                        if (-not (Test-LocalTcpPort -Port $port)) { break }
                    }
                }
            }
        }

        if (-not (Test-LocalTcpPort -Port $port) -and -not $result.agent_control.refused_unowned_listener) {
            try {
                if (-not (Test-Path -LiteralPath $serverPath)) {
                    throw "Agent Control server missing: $serverPath"
                }
                $node = (Get-Command node.exe -ErrorAction Stop).Source
                $env:AGENT_CONTROL_REPO = $RepoRoot
                $env:AGENT_CONTROL_SKIP_LOCAL_BRIDGE_BOOTSTRAP = '1'
                $env:AGENT_CONTROL_SOURCE_SHA = $ExpectedSourceSha
                $env:AGENT_CONTROL_VERSION = $ExpectedAgentControlVersion
                Start-Process -FilePath $node -ArgumentList @('server.mjs') -WorkingDirectory $agentDir -WindowStyle Hidden
                $result.agent_control.started = $true
                Write-RestoreLog ("Started Agent Control server at source {0} / v{1}." -f $ExpectedSourceSha, $ExpectedAgentControlVersion)

                for ($i = 0; $i -lt 20; $i++) {
                    Start-Sleep -Milliseconds 250
                    if (Test-LocalTcpPort -Port $port) { break }
                }
            } catch {
                $result.agent_control.error = $_.Exception.Message
                Write-RestoreLog "Agent Control start failed: $($result.agent_control.error)"
            }
        }

        if (Test-LocalTcpPort -Port $port) {
            try {
                $status = Invoke-RestMethod -Uri 'http://127.0.0.1:7331/api/status' -Method Get -TimeoutSec 3
                $identityMatches =
                    ([string]$status.controller.sourceSha -eq $ExpectedSourceSha) -and
                    ([string]$status.controller.agentControlVersion -eq $ExpectedAgentControlVersion)
                $result.agent_control.healthy = [bool]$status.ok -and $identityMatches
                if (-not $identityMatches -and -not $result.agent_control.error) {
                    $result.agent_control.error = "Agent Control responded but runtime identity did not match canonical source/version."
                }
            } catch {
                $result.agent_control.healthy = $false
                if (-not $result.agent_control.error) {
                    $result.agent_control.error = $_.Exception.Message
                }
            }
        }
    }

    $result | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $statusPath -Encoding UTF8
    Write-RestoreLog ("Restore complete: bridgeTasks={0}; agentControlHealthy={1}; agentWatchdog={2}; localPlugins={3}" -f $result.heaven_bridge.scheduled_tasks_found, $result.agent_control.healthy, $result.agent_control.watchdog_task_found, $result.plugin_workspace.manifest_count)

    $openDashboard = if ($null -eq $profile.open_dashboard) { $false } else { [bool]$profile.open_dashboard }
    if ($openDashboard -and $result.agent_control.healthy) {
        Start-Process 'http://127.0.0.1:7331'
    }

    $result | ConvertTo-Json -Depth 8 -Compress
} finally {
    if ($lockStream) {
        $lockStream.Dispose()
    }
}
