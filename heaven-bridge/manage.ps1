param(
    [Parameter(Position = 0)]
    [ValidateSet('START', 'STATUS', 'TEST', 'STOP', 'RECOVER')]
    [string]$Action = 'STATUS'
)

$ErrorActionPreference = 'Stop'

$BridgeDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RepoRoot = Split-Path -Parent $BridgeDir
$RelayRepoRoot = Join-Path $env:USERPROFILE 'HeavenBridgeRepo'
$RelayBridgeDir = Join-Path $RelayRepoRoot 'heaven-bridge'
$Bootstrap = Join-Path $BridgeDir 'bootstrap.ps1'
$GitStateHelper = Join-Path $BridgeDir 'git-state.ps1'
$WorkerSource = Join-Path $BridgeDir 'worker.py'
$WatchdogSource = Join-Path $BridgeDir 'watchdog.ps1'
$SentinelSource = Join-Path $BridgeDir 'sentinel.ps1'
$RelayWorkerSource = Join-Path $RelayBridgeDir 'worker.py'
$RelayWatchdogSource = Join-Path $RelayBridgeDir 'watchdog.ps1'
$PrimaryTests = Join-Path $BridgeDir 'test_worker.py'
$CompatibilityTests = Join-Path $BridgeDir 'tests\test_worker.py'
$GitStateTests = Join-Path $BridgeDir 'tests\Test-GitState.ps1'
$SecretEnvelopeIoTests = Join-Path $BridgeDir 'tests\Test-SecretEnvelopeIo.ps1'
$SecretEnvelopeHelper = Join-Path $BridgeDir 'New-HeavenSecretEnvelope.ps1'
$SecretEnvelopeIo = Join-Path $BridgeDir 'secret-envelope-io.ps1'
$RuntimeWorker = Join-Path $env:USERPROFILE '.mhw-local-tools\heaven-desktop-worker.py'
$RuntimeWatchdog = Join-Path $env:USERPROFILE '.mhw-local-tools\heaven-bridge-watchdog.ps1'
$RuntimeSentinel = Join-Path $env:USERPROFILE '.mhw-local-tools\heaven-bridge-sentinel.ps1'
$LocalHeartbeat = Join-Path $env:USERPROFILE 'HeavenBridge\worker-local-heartbeat.json'
$LoopProgress = Join-Path $env:USERPROFILE 'HeavenBridge\worker-loop-progress.json'
$HostId = if ($env:HEAVEN_BRIDGE_HOST) { $env:HEAVEN_BRIDGE_HOST.Trim().ToLowerInvariant() } elseif ($env:COMPUTERNAME) { $env:COMPUTERNAME.Trim().ToLowerInvariant() } else { 'heaven' }
$ScopedHeartbeat = Join-Path $RelayBridgeDir ("status\hosts\{0}\heartbeat.json" -f $HostId)
$LegacyHeartbeat = Join-Path $RelayBridgeDir 'status\heartbeat.json'

. $GitStateHelper

function Get-CanonicalWorkers {
    @(
        Get-CimInstance Win32_Process -ErrorAction SilentlyContinue |
            Where-Object {
                $_.CommandLine -and
                $_.CommandLine -like '*\.mhw-local-tools\heaven-desktop-worker.py*'
            }
    )
}

function Get-WatchdogWorkers {
    @(
        Get-CimInstance Win32_Process -ErrorAction SilentlyContinue |
            Where-Object {
                $_.CommandLine -and
                $_.CommandLine -like '*\.mhw-local-tools\heaven-bridge-watchdog.ps1*'
            }
    )
}

function Get-SentinelWorkers {
    @(
        Get-CimInstance Win32_Process -ErrorAction SilentlyContinue |
            Where-Object {
                $_.CommandLine -and
                $_.CommandLine -like '*\.mhw-local-tools\heaven-bridge-sentinel.ps1*'
            }
    )
}

function Get-LocalHeartbeatState {
    if (-not (Test-Path $LocalHeartbeat)) {
        return [ordered]@{ exists = $false; updated_at = $null; pid = $null; age_seconds = $null; parse_error = $null }
    }
    try {
        $row = Get-Content -Raw -Path $LocalHeartbeat | ConvertFrom-Json
        $updated = [DateTimeOffset]::Parse([string]$row.updated_at)
        return [ordered]@{
            exists = $true
            updated_at = [string]$row.updated_at
            pid = if ($null -eq $row.pid) { $null } else { [int]$row.pid }
            age_seconds = [Math]::Max(0, [int]([DateTimeOffset]::UtcNow - $updated).TotalSeconds)
            parse_error = $null
        }
    } catch {
        return [ordered]@{ exists = $true; updated_at = $null; pid = $null; age_seconds = $null; parse_error = $_.Exception.Message }
    }
}

function Get-LegacyWorkers {
    @(
        Get-CimInstance Win32_Process -ErrorAction SilentlyContinue |
            Where-Object {
                $_.CommandLine -and
                $_.CommandLine -like '*\HeavenBridge\relay\heaven-bridge\worker.ps1*'
            }
    )
}

function Get-TaskState {
    param([string]$Name)
    try {
        $task = Get-ScheduledTask -TaskName $Name -ErrorAction Stop
        return [string]$task.State
    } catch {
        return 'missing'
    }
}

function Get-TaskRunLevel {
    param([string]$Name)
    try {
        $task = Get-ScheduledTask -TaskName $Name -ErrorAction Stop
        return [string]$task.Principal.RunLevel
    } catch {
        return 'missing'
    }
}

function Get-LoopProgressState {
    if (-not (Test-Path $LoopProgress)) {
        return [ordered]@{ exists = $false; updated_at = $null; pid = $null; age_seconds = $null; parse_error = $null }
    }
    try {
        $row = Get-Content -Raw -Path $LoopProgress | ConvertFrom-Json
        $updated = [DateTimeOffset]::Parse([string]$row.updated_at)
        return [ordered]@{
            exists = $true
            updated_at = [string]$row.updated_at
            pid = if ($null -eq $row.pid) { $null } else { [int]$row.pid }
            age_seconds = [Math]::Max(0, [int]([DateTimeOffset]::UtcNow - $updated).TotalSeconds)
            parse_error = $null
        }
    } catch {
        return [ordered]@{ exists = $true; updated_at = $null; pid = $null; age_seconds = $null; parse_error = $_.Exception.Message }
    }
}

function Get-HeartbeatState {
    $heartbeatPath = $ScopedHeartbeat
    if (-not (Test-Path $heartbeatPath) -and $HostId -eq 'heaven' -and (Test-Path $LegacyHeartbeat)) {
        $heartbeatPath = $LegacyHeartbeat
    }
    if (-not (Test-Path $heartbeatPath)) {
        return [ordered]@{ exists = $false; host = $HostId; updated_at = $null; worker_version = $null; protocol = $null; elevated = $null; age_seconds = $null }
    }

    try {
        $row = Get-Content -Raw -Path $heartbeatPath | ConvertFrom-Json
        $updated = [DateTimeOffset]::Parse([string]$row.updated_at)
        $age = [Math]::Max(0, [int]([DateTimeOffset]::UtcNow - $updated).TotalSeconds)
        return [ordered]@{
            exists = $true
            host = [string]$row.host
            updated_at = [string]$row.updated_at
            worker_version = $row.worker_version
            protocol = [string]$row.protocol
            elevated = if ($null -eq $row.elevated) { $null } else { [bool]$row.elevated }
            age_seconds = $age
        }
    } catch {
        return [ordered]@{ exists = $true; host = $HostId; updated_at = $null; worker_version = $null; protocol = $null; elevated = $null; age_seconds = $null; parse_error = $_.Exception.Message }
    }
}

function Show-Status {
    $canonical = @(Get-CanonicalWorkers)
    $watchdogs = @(Get-WatchdogWorkers)
    $sentinels = @(Get-SentinelWorkers)
    $legacy = @(Get-LegacyWorkers)

    $branch = $null
    $gitStatus = @()
    if (Test-Path (Join-Path $RelayRepoRoot '.git')) {
        $branch = Get-HeavenBridgeGitCurrentBranch -Repository $RelayRepoRoot
        $gitStatus = @(& git -C $RelayRepoRoot status --short)
        if ($LASTEXITCODE -ne 0) { throw 'Unable to read Heaven Bridge relay status.' }
    }

    $sourceHash = $null
    $runtimeHash = $null
    $watchdogSourceHash = $null
    $watchdogRuntimeHash = $null
    $sentinelSourceHash = $null
    $sentinelRuntimeHash = $null
    if (Test-Path $RelayWorkerSource) {
        $sourceHash = (Get-FileHash $RelayWorkerSource -Algorithm SHA256).Hash
    }
    if (Test-Path $RuntimeWorker) {
        $runtimeHash = (Get-FileHash $RuntimeWorker -Algorithm SHA256).Hash
    }
    if (Test-Path $RelayWatchdogSource) {
        $watchdogSourceHash = (Get-FileHash $RelayWatchdogSource -Algorithm SHA256).Hash
    }
    if (Test-Path $RuntimeWatchdog) {
        $watchdogRuntimeHash = (Get-FileHash $RuntimeWatchdog -Algorithm SHA256).Hash
    }
    if (Test-Path $SentinelSource) {
        $sentinelSourceHash = (Get-FileHash $SentinelSource -Algorithm SHA256).Hash
    }
    if (Test-Path $RuntimeSentinel) {
        $sentinelRuntimeHash = (Get-FileHash $RuntimeSentinel -Algorithm SHA256).Hash
    }

    $heartbeat = Get-HeartbeatState
    $localHeartbeat = Get-LocalHeartbeatState
    $loopProgress = Get-LoopProgressState
    $canonicalRunLevel = Get-TaskRunLevel 'Heaven Local Bridge'
    $watchdogRunLevel = Get-TaskRunLevel 'Heaven Local Bridge Watchdog'
    $sentinelRunLevel = Get-TaskRunLevel 'Heaven Local Bridge Sentinel'
    $canonicalPid = if ($canonical.Count -eq 1) { [int]$canonical[0].ProcessId } else { $null }
    $healthy = (
        $canonical.Count -eq 1 -and
        $watchdogs.Count -eq 1 -and
        $sentinels.Count -eq 1 -and
        $legacy.Count -eq 0 -and
        $branch -eq 'heaven-bridge' -and
        $sourceHash -and
        $sourceHash -eq $runtimeHash -and
        $watchdogSourceHash -and
        $watchdogSourceHash -eq $watchdogRuntimeHash -and
        $sentinelSourceHash -and
        $sentinelSourceHash -eq $sentinelRuntimeHash -and
        $canonicalRunLevel -eq 'Highest' -and
        $watchdogRunLevel -eq 'Highest' -and
        $sentinelRunLevel -eq 'Highest' -and
        $localHeartbeat.exists -and
        $localHeartbeat.parse_error -eq $null -and
        $localHeartbeat.pid -eq $canonicalPid -and
        $localHeartbeat.age_seconds -ne $null -and
        $localHeartbeat.age_seconds -le 120 -and
        $loopProgress.exists -and
        $loopProgress.parse_error -eq $null -and
        $loopProgress.pid -eq $canonicalPid -and
        $loopProgress.age_seconds -ne $null -and
        $loopProgress.age_seconds -le 900 -and
        $heartbeat.exists -and
        $heartbeat.host -eq $HostId -and
        $heartbeat.protocol -eq 'chatgpt-heaven-bridge-v2' -and
        $heartbeat.elevated -eq $true -and
        $heartbeat.age_seconds -ne $null -and
        $heartbeat.age_seconds -le 900
    )

    $report = [ordered]@{
        healthy = [bool]$healthy
        host = $HostId
        repo = $RelayRepoRoot
        branch = $branch
        tracked_or_untracked_changes = $gitStatus
        canonical_worker_count = $canonical.Count
        canonical_worker_pids = @($canonical | ForEach-Object { [int]$_.ProcessId })
        watchdog_count = $watchdogs.Count
        watchdog_pids = @($watchdogs | ForEach-Object { [int]$_.ProcessId })
        sentinel_count = $sentinels.Count
        sentinel_pids = @($sentinels | ForEach-Object { [int]$_.ProcessId })
        legacy_worker_count = $legacy.Count
        legacy_worker_pids = @($legacy | ForEach-Object { [int]$_.ProcessId })
        source_matches_runtime = [bool]($sourceHash -and $sourceHash -eq $runtimeHash)
        watchdog_source_matches_runtime = [bool]($watchdogSourceHash -and $watchdogSourceHash -eq $watchdogRuntimeHash)
        sentinel_source_matches_runtime = [bool]($sentinelSourceHash -and $sentinelSourceHash -eq $sentinelRuntimeHash)
        local_heartbeat = $localHeartbeat
        loop_progress = $loopProgress
        heartbeat = $heartbeat
        scheduled_task = @{
            canonical = Get-TaskState 'Heaven Local Bridge'
            canonical_run_level = $canonicalRunLevel
            watchdog = Get-TaskState 'Heaven Local Bridge Watchdog'
            watchdog_run_level = $watchdogRunLevel
            sentinel = Get-TaskState 'Heaven Local Bridge Sentinel'
            sentinel_run_level = $sentinelRunLevel
            legacy = Get-TaskState 'HeavenLocalBridge'
            legacy_run_level = Get-TaskRunLevel 'HeavenLocalBridge'
        }
        startup_fallback = Test-Path (Join-Path ([Environment]::GetFolderPath('Startup')) 'HeavenBridgeWatchdog.vbs')
        legacy_worker_startup_fallback = Test-Path (Join-Path ([Environment]::GetFolderPath('Startup')) 'HeavenBridgeWorker.vbs')
    }

    $report | ConvertTo-Json -Depth 6
    if (-not $healthy) { exit 2 }
}

function Invoke-Tests {
    $python = (Get-Command python.exe -ErrorAction Stop).Source

    & $python -m py_compile $WorkerSource $PrimaryTests $CompatibilityTests
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    & $python -m unittest -v $PrimaryTests
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    & $python -m unittest -v $CompatibilityTests
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    & $GitStateTests
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    & $SecretEnvelopeIoTests
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    foreach ($path in @($Bootstrap, $PSCommandPath, $WatchdogSource, $SentinelSource, $GitStateHelper, $GitStateTests, $SecretEnvelopeHelper, $SecretEnvelopeIo, $SecretEnvelopeIoTests)) {
        $tokens = $null
        $errors = $null
        [void][System.Management.Automation.Language.Parser]::ParseFile(
            (Resolve-Path $path),
            [ref]$tokens,
            [ref]$errors
        )
        if ($errors.Count -gt 0) {
            $errors | Format-List | Out-String | Write-Error
            exit 1
        }
    }

    Write-Output 'HEAVEN_BRIDGE_TESTS_OK'
}

switch ($Action) {
    'START' {
        if (
            @(Get-CanonicalWorkers).Count -ne 1 -or
            @(Get-WatchdogWorkers).Count -ne 1 -or
            @(Get-SentinelWorkers).Count -ne 1 -or
            (Get-TaskRunLevel 'Heaven Local Bridge') -ne 'Highest' -or
            (Get-TaskRunLevel 'Heaven Local Bridge Watchdog') -ne 'Highest' -or
            (Get-TaskRunLevel 'Heaven Local Bridge Sentinel') -ne 'Highest'
        ) {
            & $Bootstrap
        }
        Show-Status
    }

    'STATUS' {
        Show-Status
    }

    'TEST' {
        Invoke-Tests
    }

    'STOP' {
        # Stop the watchdog first or it can correctly interpret the intentional
        # worker shutdown as a failure and immediately resurrect it.
        foreach ($taskName in @('Heaven Local Bridge Sentinel', 'Heaven Local Bridge Watchdog', 'Heaven Local Bridge')) {
            try { Stop-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue } catch {}
        }
        foreach ($proc in @(Get-SentinelWorkers)) {
            try { Stop-Process -Id $proc.ProcessId -Force -ErrorAction Stop } catch {
                Write-Warning "Failed to stop sentinel PID $($proc.ProcessId): $($_.Exception.Message)"
            }
        }
        foreach ($proc in @(Get-WatchdogWorkers)) {
            try { Stop-Process -Id $proc.ProcessId -Force -ErrorAction Stop } catch {
                Write-Warning "Failed to stop watchdog PID $($proc.ProcessId): $($_.Exception.Message)"
            }
        }
        foreach ($proc in @(Get-CanonicalWorkers)) {
            try { Stop-Process -Id $proc.ProcessId -Force -ErrorAction Stop } catch {
                Write-Warning "Failed to stop canonical worker PID $($proc.ProcessId): $($_.Exception.Message)"
            }
        }
        Start-Sleep -Milliseconds 500
        if (@(Get-SentinelWorkers).Count -gt 0) {
            throw 'One or more Heaven Bridge sentinels are still running.'
        }
        if (@(Get-WatchdogWorkers).Count -gt 0) {
            throw 'One or more Heaven Bridge watchdogs are still running.'
        }
        if (@(Get-CanonicalWorkers).Count -gt 0) {
            throw 'One or more canonical Heaven Bridge workers are still running.'
        }
        Remove-Item $LocalHeartbeat -Force -ErrorAction SilentlyContinue
        Remove-Item $LoopProgress -Force -ErrorAction SilentlyContinue
        Write-Output 'HEAVEN_BRIDGE_STOPPED'
    }

    'RECOVER' {
        & $Bootstrap
        Show-Status
    }
}
