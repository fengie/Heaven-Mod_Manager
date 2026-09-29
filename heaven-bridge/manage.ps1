param(
    [Parameter(Position = 0)]
    [ValidateSet('START', 'STATUS', 'TEST', 'STOP', 'RECOVER')]
    [string]$Action = 'STATUS'
)

$ErrorActionPreference = 'Stop'

$BridgeDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RepoRoot = Split-Path -Parent $BridgeDir
$Bootstrap = Join-Path $BridgeDir 'bootstrap.ps1'
$WorkerSource = Join-Path $BridgeDir 'worker.py'
$PrimaryTests = Join-Path $BridgeDir 'test_worker.py'
$CompatibilityTests = Join-Path $BridgeDir 'tests\test_worker.py'
$SecretEnvelopeIoTests = Join-Path $BridgeDir 'tests\Test-SecretEnvelopeIo.ps1'
$SecretEnvelopeHelper = Join-Path $BridgeDir 'New-HeavenSecretEnvelope.ps1'
$SecretEnvelopeIo = Join-Path $BridgeDir 'secret-envelope-io.ps1'
$RuntimeWorker = Join-Path $env:USERPROFILE '.mhw-local-tools\heaven-desktop-worker.py'
$HostId = if ($env:HEAVEN_BRIDGE_HOST) { $env:HEAVEN_BRIDGE_HOST.Trim().ToLowerInvariant() } elseif ($env:COMPUTERNAME) { $env:COMPUTERNAME.Trim().ToLowerInvariant() } else { 'heaven' }
$ScopedHeartbeat = Join-Path $BridgeDir ("status\hosts\{0}\heartbeat.json" -f $HostId)
$LegacyHeartbeat = Join-Path $BridgeDir 'status\heartbeat.json'

function Get-CanonicalWorkers {
    @(
        Get-CimInstance Win32_Process -ErrorAction SilentlyContinue |
            Where-Object {
                $_.CommandLine -and
                $_.CommandLine -like '*\.mhw-local-tools\heaven-desktop-worker.py*'
            }
    )
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
    $legacy = @(Get-LegacyWorkers)

    $branch = $null
    $gitStatus = @()
    if (Test-Path (Join-Path $RepoRoot '.git')) {
        $branch = (& git -C $RepoRoot branch --show-current).Trim()
        if ($LASTEXITCODE -ne 0) { throw 'Unable to read Heaven Bridge relay branch.' }
        $gitStatus = @(& git -C $RepoRoot status --short)
        if ($LASTEXITCODE -ne 0) { throw 'Unable to read Heaven Bridge relay status.' }
    }

    $sourceHash = $null
    $runtimeHash = $null
    if (Test-Path $WorkerSource) {
        $sourceHash = (Get-FileHash $WorkerSource -Algorithm SHA256).Hash
    }
    if (Test-Path $RuntimeWorker) {
        $runtimeHash = (Get-FileHash $RuntimeWorker -Algorithm SHA256).Hash
    }

    $heartbeat = Get-HeartbeatState
    $canonicalRunLevel = Get-TaskRunLevel 'Heaven Local Bridge'
    $healthy = (
        $canonical.Count -eq 1 -and
        $legacy.Count -eq 0 -and
        $branch -eq 'heaven-bridge' -and
        $sourceHash -and
        $sourceHash -eq $runtimeHash -and
        $canonicalRunLevel -eq 'Highest' -and
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
        repo = $RepoRoot
        branch = $branch
        tracked_or_untracked_changes = $gitStatus
        canonical_worker_count = $canonical.Count
        canonical_worker_pids = @($canonical | ForEach-Object { [int]$_.ProcessId })
        legacy_worker_count = $legacy.Count
        legacy_worker_pids = @($legacy | ForEach-Object { [int]$_.ProcessId })
        source_matches_runtime = [bool]($sourceHash -and $sourceHash -eq $runtimeHash)
        heartbeat = $heartbeat
        scheduled_task = @{
            canonical = Get-TaskState 'Heaven Local Bridge'
            canonical_run_level = $canonicalRunLevel
            legacy = Get-TaskState 'HeavenLocalBridge'
            legacy_run_level = Get-TaskRunLevel 'HeavenLocalBridge'
        }
        startup_fallback = Test-Path (Join-Path ([Environment]::GetFolderPath('Startup')) 'HeavenBridgeWorker.vbs')
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

    & $SecretEnvelopeIoTests
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    foreach ($path in @($Bootstrap, $PSCommandPath, $SecretEnvelopeHelper, $SecretEnvelopeIo, $SecretEnvelopeIoTests)) {
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
        if (@(Get-CanonicalWorkers).Count -eq 0) {
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
        foreach ($taskName in @('Heaven Local Bridge')) {
            try { Stop-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue } catch {}
        }
        foreach ($proc in @(Get-CanonicalWorkers)) {
            try { Stop-Process -Id $proc.ProcessId -Force -ErrorAction Stop } catch {
                Write-Warning "Failed to stop canonical worker PID $($proc.ProcessId): $($_.Exception.Message)"
            }
        }
        Start-Sleep -Milliseconds 500
        if (@(Get-CanonicalWorkers).Count -gt 0) {
            throw 'One or more canonical Heaven Bridge workers are still running.'
        }
        Write-Output 'HEAVEN_BRIDGE_STOPPED'
    }

    'RECOVER' {
        & $Bootstrap
        Show-Status
    }
}
