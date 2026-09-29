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
$RuntimeWorker = Join-Path $env:USERPROFILE '.mhw-local-tools\heaven-desktop-worker.py'
$Heartbeat = Join-Path $BridgeDir 'status\heartbeat.json'

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
    if (-not (Test-Path $Heartbeat)) {
        return [ordered]@{ exists = $false; updated_at = $null; worker_version = $null; protocol = $null; age_seconds = $null }
    }

    try {
        $row = Get-Content -Raw -Path $Heartbeat | ConvertFrom-Json
        $updated = [DateTimeOffset]::Parse([string]$row.updated_at)
        $age = [Math]::Max(0, [int]([DateTimeOffset]::UtcNow - $updated).TotalSeconds)
        return [ordered]@{
            exists = $true
            updated_at = [string]$row.updated_at
            worker_version = $row.worker_version
            protocol = [string]$row.protocol
            age_seconds = $age
        }
    } catch {
        return [ordered]@{ exists = $true; updated_at = $null; worker_version = $null; protocol = $null; age_seconds = $null; parse_error = $_.Exception.Message }
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
    $healthy = (
        $canonical.Count -eq 1 -and
        $legacy.Count -eq 0 -and
        $branch -eq 'heaven-bridge' -and
        $sourceHash -and
        $sourceHash -eq $runtimeHash -and
        $heartbeat.exists -and
        $heartbeat.protocol -eq 'chatgpt-heaven-bridge-v2' -and
        $heartbeat.age_seconds -ne $null -and
        $heartbeat.age_seconds -le 900
    )

    $report = [ordered]@{
        healthy = [bool]$healthy
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
            canonical_run_level = Get-TaskRunLevel 'Heaven Local Bridge'
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

    foreach ($path in @($Bootstrap, $PSCommandPath)) {
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
