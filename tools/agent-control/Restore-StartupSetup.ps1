param(
    [string]$RepoRoot = $env:AGENT_CONTROL_REPO,
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
            already_listening = $false
            started = $false
            healthy = $false
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

    $agentControlEnabled = if ($null -eq $profile.agent_control) { $true } else { [bool]$profile.agent_control }
    if ($agentControlEnabled) {
        $result.agent_control.requested = $true
        $port = 7331
        $result.agent_control.already_listening = Test-LocalTcpPort -Port $port

        if (-not $result.agent_control.already_listening) {
            try {
                $agentDir = Join-Path $RepoRoot 'tools\agent-control'
                $server = Join-Path $agentDir 'server.mjs'
                if (-not (Test-Path -LiteralPath $server)) {
                    throw "Agent Control server missing: $server"
                }
                $node = (Get-Command node.exe -ErrorAction Stop).Source
                $env:AGENT_CONTROL_REPO = $RepoRoot
                $env:AGENT_CONTROL_SKIP_LOCAL_BRIDGE_BOOTSTRAP = '1'
                Start-Process -FilePath $node -ArgumentList @('server.mjs') -WorkingDirectory $agentDir -WindowStyle Hidden
                $result.agent_control.started = $true
                Write-RestoreLog 'Started Agent Control server.'

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
                $result.agent_control.healthy = [bool]$status.ok
            } catch {
                $result.agent_control.healthy = $false
                if (-not $result.agent_control.error) {
                    $result.agent_control.error = $_.Exception.Message
                }
            }
        }
    }

    $result | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $statusPath -Encoding UTF8
    Write-RestoreLog ("Restore complete: bridgeTasks={0}; agentControlHealthy={1}; localPlugins={2}" -f $result.heaven_bridge.scheduled_tasks_found, $result.agent_control.healthy, $result.plugin_workspace.manifest_count)

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
