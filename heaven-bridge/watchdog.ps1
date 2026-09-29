param(
    [switch]$Once,
    [switch]$DryRun,
    [ValidateRange(5, 300)]
    [int]$IntervalSeconds = 30,
    [ValidateRange(30, 900)]
    [int]$StaleSeconds = 120
)

$ErrorActionPreference = 'Stop'

$TaskName = 'Heaven Local Bridge'
$RuntimeDir = Split-Path -Parent $PSCommandPath
$RuntimeWorker = Join-Path $RuntimeDir 'heaven-desktop-worker.py'
$StateDir = Join-Path $env:USERPROFILE 'HeavenBridge'
$LocalHeartbeat = Join-Path $StateDir 'worker-local-heartbeat.json'
$WatchdogLog = Join-Path $StateDir 'watchdog.log'
$MutexName = 'Local\MHW.HeavenBridgeWatchdog'
$StartupGraceSeconds = 45

New-Item -ItemType Directory -Force -Path $StateDir | Out-Null

function Write-WatchdogLog {
    param([Parameter(Mandatory = $true)][string]$Message)
    $line = '{0} {1}' -f ([DateTimeOffset]::UtcNow.ToString('o')), $Message
    Add-Content -Path $WatchdogLog -Value $line -Encoding UTF8
}

function Get-CanonicalWorkers {
    @(
        Get-CimInstance Win32_Process -ErrorAction SilentlyContinue |
            Where-Object {
                $_.CommandLine -and
                $_.CommandLine -like '*\.mhw-local-tools\heaven-desktop-worker.py*'
            } |
            Sort-Object CreationDate
    )
}

function Get-WorkerAgeSeconds {
    param($Worker)
    try {
        $created = [System.Management.ManagementDateTimeConverter]::ToDateTime([string]$Worker.CreationDate)
        return [Math]::Max(0, [int]((Get-Date) - $created).TotalSeconds)
    } catch {
        return $null
    }
}

function Get-LocalHeartbeatState {
    if (-not (Test-Path $LocalHeartbeat)) {
        return [ordered]@{ exists = $false; age_seconds = $null; pid = $null; parse_error = $null }
    }

    try {
        $row = Get-Content -Raw -Path $LocalHeartbeat | ConvertFrom-Json
        $updated = [DateTimeOffset]::Parse([string]$row.updated_at)
        return [ordered]@{
            exists = $true
            age_seconds = [Math]::Max(0, [int]([DateTimeOffset]::UtcNow - $updated).TotalSeconds)
            pid = if ($null -eq $row.pid) { $null } else { [int]$row.pid }
            parse_error = $null
        }
    } catch {
        return [ordered]@{ exists = $true; age_seconds = $null; pid = $null; parse_error = $_.Exception.Message }
    }
}

function Start-WorkerSafely {
    if ($DryRun) {
        Write-WatchdogLog 'DRY-RUN: worker start requested.'
        return
    }

    try {
        Start-ScheduledTask -TaskName $TaskName -ErrorAction Stop
        Start-Sleep -Seconds 4
    } catch {
        Write-WatchdogLog ("Scheduled-task start failed: {0}" -f $_.Exception.Message)
    }

    if (@(Get-CanonicalWorkers).Count -gt 0) {
        return
    }

    if (-not (Test-Path $RuntimeWorker)) {
        throw "Runtime worker is missing: $RuntimeWorker"
    }

    $pythonw = (Get-Command pythonw.exe -ErrorAction Stop).Source
    Start-Process -WindowStyle Hidden -FilePath $pythonw -ArgumentList ('"{0}"' -f $RuntimeWorker) -WorkingDirectory $env:USERPROFILE | Out-Null
    Start-Sleep -Seconds 4
}

function Restart-WorkerSafely {
    param([Parameter(Mandatory = $true)][string]$Reason)

    Write-WatchdogLog ("Repairing worker: {0}" -f $Reason)
    if ($DryRun) {
        Write-WatchdogLog 'DRY-RUN: worker restart requested.'
        return
    }

    try { Stop-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue } catch {}
    foreach ($proc in @(Get-CanonicalWorkers)) {
        try { Stop-Process -Id ([int]$proc.ProcessId) -Force -ErrorAction Stop } catch {
            Write-WatchdogLog ("Failed to stop stale worker PID {0}: {1}" -f $proc.ProcessId, $_.Exception.Message)
        }
    }
    Start-Sleep -Seconds 1
    Start-WorkerSafely
}

function Invoke-WatchdogCheck {
    $workers = @(Get-CanonicalWorkers)
    if ($workers.Count -eq 0) {
        Write-WatchdogLog 'No canonical worker found.'
        Start-WorkerSafely
        return
    }

    if ($workers.Count -gt 1) {
        Write-WatchdogLog ("Observed {0} canonical worker processes; singleton lock should converge them." -f $workers.Count)
        return
    }

    $worker = $workers[0]
    $workerAge = Get-WorkerAgeSeconds -Worker $worker
    if ($workerAge -ne $null -and $workerAge -lt $StartupGraceSeconds) {
        return
    }

    $heartbeat = Get-LocalHeartbeatState
    if (-not $heartbeat.exists) {
        if ($workerAge -ne $null -and $workerAge -ge $StaleSeconds) {
            Restart-WorkerSafely -Reason "local heartbeat missing for worker PID $($worker.ProcessId)"
        }
        return
    }

    if ($heartbeat.parse_error) {
        if ($workerAge -ne $null -and $workerAge -ge $StaleSeconds) {
            Restart-WorkerSafely -Reason "local heartbeat unreadable: $($heartbeat.parse_error)"
        }
        return
    }

    if ($heartbeat.pid -ne [int]$worker.ProcessId) {
        if ($workerAge -ne $null -and $workerAge -ge $StartupGraceSeconds) {
            Restart-WorkerSafely -Reason "local heartbeat PID $($heartbeat.pid) does not match live worker PID $($worker.ProcessId)"
        }
        return
    }

    if ($heartbeat.age_seconds -ne $null -and $heartbeat.age_seconds -gt $StaleSeconds) {
        Restart-WorkerSafely -Reason "local heartbeat stale by $($heartbeat.age_seconds)s"
    }
}

$mutex = New-Object System.Threading.Mutex($false, $MutexName)
$acquired = $false
try {
    try {
        $acquired = $mutex.WaitOne(0)
    } catch [System.Threading.AbandonedMutexException] {
        $acquired = $true
    }

    if (-not $acquired) {
        Write-WatchdogLog 'Another watchdog instance already owns the singleton mutex; exiting.'
        exit 0
    }

    Write-WatchdogLog ("Watchdog started once={0} dryRun={1} interval={2}s stale={3}s" -f $Once, $DryRun, $IntervalSeconds, $StaleSeconds)
    do {
        try {
            Invoke-WatchdogCheck
        } catch {
            Write-WatchdogLog ("Watchdog check failed: {0}" -f $_.Exception.Message)
        }

        if ($Once) { break }
        Start-Sleep -Seconds $IntervalSeconds
    } while ($true)
} finally {
    if ($acquired) {
        try { $mutex.ReleaseMutex() } catch {}
    }
    $mutex.Dispose()
}
