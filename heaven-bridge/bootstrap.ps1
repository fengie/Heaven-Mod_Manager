$ErrorActionPreference = 'Stop'

$RepoRoot = Join-Path $env:USERPROFILE 'HeavenBridgeRepo'
$RepoUrl = 'https://github.com/fengie/mhw-mods.git'
$Branch = 'heaven-bridge'
$RuntimeDir = Join-Path $env:USERPROFILE '.mhw-local-tools'
$RuntimeWorker = Join-Path $RuntimeDir 'heaven-desktop-worker.py'
$BackupWorker = Join-Path $RuntimeDir 'heaven-desktop-worker.py.bak'
$RuntimeWatchdog = Join-Path $RuntimeDir 'heaven-bridge-watchdog.ps1'
$SourceWorker = Join-Path $RepoRoot 'heaven-bridge\worker.py'
$SourceWatchdog = Join-Path $RepoRoot 'heaven-bridge\watchdog.ps1'
$Startup = [Environment]::GetFolderPath('Startup')
$StartupVbs = Join-Path $Startup 'HeavenBridgeWorker.vbs'
$StartupWatchdogVbs = Join-Path $Startup 'HeavenBridgeWatchdog.vbs'
$TaskName = 'Heaven Local Bridge'
$WatchdogTaskName = 'Heaven Local Bridge Watchdog'
$GitStateHelper = Join-Path $PSScriptRoot 'git-state.ps1'

. $GitStateHelper

function Test-IsElevated {
    $identity = [System.Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object System.Security.Principal.WindowsPrincipal($identity)
    return $principal.IsInRole([System.Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Ensure-ElevatedBootstrap {
    if (Test-IsElevated) { return }
    if ([string]::IsNullOrWhiteSpace($PSCommandPath)) {
        throw 'Heaven Bridge bootstrap needs elevation but PSCommandPath is unavailable.'
    }

    $hostExe = (Get-Process -Id $PID -ErrorAction Stop).Path
    $args = '-NoProfile -ExecutionPolicy Bypass -File "{0}"' -f $PSCommandPath
    Write-Output 'HEAVEN_BRIDGE_ELEVATION_REQUESTED'
    $child = Start-Process -FilePath $hostExe -Verb RunAs -ArgumentList $args -Wait -PassThru
    exit $child.ExitCode
}

Ensure-ElevatedBootstrap

$env:GIT_TERMINAL_PROMPT = '0'
$RecoveryDir = Join-Path (Join-Path $env:USERPROFILE 'HeavenBridge') 'bootstrap-recovery'
New-Item -ItemType Directory -Force -Path $RuntimeDir,$RecoveryDir | Out-Null

function Invoke-GitChecked {
    param([Parameter(Mandatory = $true)][string[]]$GitArgs)
    & git -C $RepoRoot @GitArgs
    if ($LASTEXITCODE -ne 0) {
        throw ('git {0} failed with exit code {1}' -f ($GitArgs -join ' '), $LASTEXITCODE)
    }
}

function Preserve-RelayCheckout {
    param([Parameter(Mandatory = $true)][string]$Reason)

    $stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
    $backupBranch = "heaven-bridge-recovery-$stamp"
    $recordDir = Join-Path $RecoveryDir $stamp
    New-Item -ItemType Directory -Force -Path $recordDir | Out-Null

    $head = (& git -C $RepoRoot rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($head)) {
        throw 'Unable to identify current relay HEAD before recovery.'
    }

    & git -C $RepoRoot branch $backupBranch $head
    if ($LASTEXITCODE -ne 0) {
        throw "Unable to preserve relay HEAD on backup branch $backupBranch."
    }

    (& git -C $RepoRoot status --short) | Set-Content -Path (Join-Path $recordDir 'status.txt') -Encoding UTF8
    (& git -C $RepoRoot diff --binary) | Set-Content -Path (Join-Path $recordDir 'working-tree.patch') -Encoding UTF8
    @{
        reason = $Reason
        preserved_at = (Get-Date).ToUniversalTime().ToString('o')
        repo = $RepoRoot
        head = $head
        backup_branch = $backupBranch
    } | ConvertTo-Json | Set-Content -Path (Join-Path $recordDir 'recovery.json') -Encoding UTF8

    return $backupBranch
}

if (-not (Test-Path (Join-Path $RepoRoot '.git'))) {
    git clone --branch $Branch --single-branch $RepoUrl $RepoRoot
    if ($LASTEXITCODE -ne 0) { throw "git clone failed with exit code $LASTEXITCODE" }
} else {
    Invoke-GitChecked @('fetch', 'origin', $Branch)

    $currentBranch = Get-HeavenBridgeGitCurrentBranch -Repository $RepoRoot
    if ($LASTEXITCODE -ne 0) { throw 'Unable to read relay branch.' }

    $trackedDirty = @(& git -C $RepoRoot status --porcelain --untracked-files=no)
    if ($LASTEXITCODE -ne 0) { throw 'Unable to inspect relay working tree.' }

    $countsText = (& git -C $RepoRoot rev-list --left-right --count "origin/$Branch...HEAD").Trim()
    if ($LASTEXITCODE -ne 0) { throw 'Unable to measure relay divergence.' }
    $counts = @($countsText -split '\s+')
    if ($counts.Count -lt 2) { throw "Unexpected relay divergence output: $countsText" }
    $behind = [int]$counts[0]
    $ahead = [int]$counts[1]

    if ($currentBranch -ne $Branch -or $trackedDirty.Count -gt 0 -or $ahead -gt 0) {
        $reason = "branch=$currentBranch trackedDirty=$($trackedDirty.Count) ahead=$ahead behind=$behind"
        $backupBranch = Preserve-RelayCheckout -Reason $reason
        Write-Warning "Preserved non-clean relay checkout as $backupBranch before recovery."

        Invoke-GitChecked @('reset', '--hard', 'HEAD')
        Invoke-GitChecked @('checkout', '-B', $Branch, "origin/$Branch")
        Invoke-GitChecked @('reset', '--hard', "origin/$Branch")
    } else {
        Invoke-GitChecked @('checkout', $Branch)
        Invoke-GitChecked @('merge', '--ff-only', "origin/$Branch")
    }
}

if (-not (Test-Path $SourceWorker)) {
    throw "Bridge worker source missing: $SourceWorker"
}
if (-not (Test-Path $SourceWatchdog)) {
    throw "Bridge watchdog source missing: $SourceWatchdog"
}

$python = (Get-Command python.exe -ErrorAction Stop).Source
$pythonw = (Get-Command pythonw.exe -ErrorAction Stop).Source
$powershell = (Get-Command powershell.exe -ErrorAction Stop).Source

$watchdogTokens = $null
$watchdogErrors = $null
[void][System.Management.Automation.Language.Parser]::ParseFile(
    (Resolve-Path $SourceWatchdog),
    [ref]$watchdogTokens,
    [ref]$watchdogErrors
)
if ($watchdogErrors.Count -gt 0) {
    $watchdogErrors | Format-List | Out-String | Write-Error
    throw 'Watchdog syntax validation failed.'
}

$staged = Join-Path $RuntimeDir 'heaven-desktop-worker.py.new'
Copy-Item $SourceWorker $staged -Force

& $python -m py_compile $staged
if ($LASTEXITCODE -ne 0) {
    Remove-Item $staged -Force -ErrorAction SilentlyContinue
    throw 'Worker syntax validation failed.'
}

# Validate the exact repository source before any running worker is stopped.
# This is intentionally stronger than syntax-only validation: bootstrap must not
# trade a working control path for an untested replacement.
$TestWorker = Join-Path $RepoRoot 'heaven-bridge\test_worker.py'
if (Test-Path $TestWorker) {
    Push-Location $RepoRoot
    try {
        & $python -m unittest -q 'heaven-bridge\test_worker.py'
        if ($LASTEXITCODE -ne 0) {
            throw "Worker regression suite failed with exit code $LASTEXITCODE."
        }
    } finally {
        Pop-Location
    }
}

# Publish the validated runtime only after the full regression suite passes.
if (Test-Path $RuntimeWorker) {
    Copy-Item $RuntimeWorker $BackupWorker -Force
}
Move-Item $staged $RuntimeWorker -Force
Copy-Item $SourceWatchdog $RuntimeWatchdog -Force

# Startup-folder recovery is deliberately independent of Task Scheduler.
# Retire the legacy direct-worker fallback: at logon it could beat the Highest
# scheduled task and hold the singleton with a non-elevated worker.
Remove-Item $StartupVbs -Force -ErrorAction SilentlyContinue

$escapedPowerShell = $powershell.Replace('"', '""')
$escapedWatchdog = $RuntimeWatchdog.Replace('"', '""')
$watchdogVbs = @"
Set sh = CreateObject("WScript.Shell")
sh.Run """$escapedPowerShell"" -NoProfile -ExecutionPolicy Bypass -File ""$escapedWatchdog"" -StartupFallback", 0, False
"@
Set-Content -Path $StartupWatchdogVbs -Value $watchdogVbs -Encoding ASCII

# Prefer Task Scheduler because it supports restart-on-failure. The canonical task
# runs in the currently logged-in user's interactive session at RunLevel Highest.
# This preserves GUI/desktop access while giving bridge commands the user's full
# elevated administrator token. Windows still enforces the one-time security
# boundary when creating/upgrading this task; after it exists, normal bridge
# jobs do not need per-command UAC elevation.
$taskInstalled = $false
$taskRunLevel = 'Unavailable'
$watchdogTaskInstalled = $false
$watchdogRunLevel = 'Unavailable'

try {
    $identityName = [System.Security.Principal.WindowsIdentity]::GetCurrent().Name
    if ([string]::IsNullOrWhiteSpace($identityName)) {
        throw 'Unable to resolve the current Windows identity for the scheduled-task principal.'
    }

    $trigger = New-ScheduledTaskTrigger -AtLogOn
    $settings = New-ScheduledTaskSettingsSet `
        -AllowStartIfOnBatteries `
        -DontStopIfGoingOnBatteries `
        -StartWhenAvailable `
        -MultipleInstances IgnoreNew `
        -RestartCount 255 `
        -RestartInterval (New-TimeSpan -Minutes 1) `
        -ExecutionTimeLimit ([TimeSpan]::Zero)
    $principal = New-ScheduledTaskPrincipal `
        -UserId $identityName `
        -LogonType Interactive `
        -RunLevel Highest

    # Always refresh the definition. Keeping an old Highest task would preserve
    # stale restart limits or Windows' default 72-hour execution limit.
    $workerAction = New-ScheduledTaskAction -Execute $pythonw -Argument ('"{0}"' -f $RuntimeWorker)
    Register-ScheduledTask `
        -TaskName $TaskName `
        -Action $workerAction `
        -Trigger $trigger `
        -Settings $settings `
        -Principal $principal `
        -Force | Out-Null

    $taskRunLevel = [string](Get-ScheduledTask -TaskName $TaskName -ErrorAction Stop).Principal.RunLevel
    if ($taskRunLevel -ne 'Highest') {
        throw "Scheduled task registered but reports unexpected RunLevel '$taskRunLevel'."
    }
    $taskInstalled = $true

    $watchdogAction = New-ScheduledTaskAction `
        -Execute $powershell `
        -Argument ('-NoProfile -ExecutionPolicy Bypass -File "{0}"' -f $RuntimeWatchdog)
    Register-ScheduledTask `
        -TaskName $WatchdogTaskName `
        -Action $watchdogAction `
        -Trigger $trigger `
        -Settings $settings `
        -Principal $principal `
        -Force | Out-Null

    $watchdogRunLevel = [string](Get-ScheduledTask -TaskName $WatchdogTaskName -ErrorAction Stop).Principal.RunLevel
    if ($watchdogRunLevel -ne 'Highest') {
        throw "Watchdog task registered but reports unexpected RunLevel '$watchdogRunLevel'."
    }
    $watchdogTaskInstalled = $true
} catch {
    Write-Warning "Highest-privilege Scheduled Task install/upgrade failed; independent Startup fallbacks remain configured. A one-time elevated registration is required before both tasks can hold an administrator token. $($_.Exception.Message)"
}

function Get-HeavenBridgeWorker {
    Get-CimInstance Win32_Process |
        Where-Object {
            $_.CommandLine -and (
                $_.CommandLine -like '*\.mhw-local-tools\heaven-desktop-worker.py*' -or
                $_.CommandLine -like '*\heaven-bridge\worker.py*'
            )
        } |
        Sort-Object CreationDate -Descending |
        Select-Object -First 1
}

function Get-HeavenBridgeWatchdog {
    Get-CimInstance Win32_Process -ErrorAction SilentlyContinue |
        Where-Object {
            $_.CommandLine -and
            $_.CommandLine -like '*\.mhw-local-tools\heaven-bridge-watchdog.ps1*'
        } |
        Sort-Object CreationDate -Descending |
        Select-Object -First 1
}

# Stop the old watchdog before intentionally replacing the worker. Otherwise a
# healthy watchdog can correctly interpret bootstrap's handoff as a crash and
# race the replacement.
try { Stop-ScheduledTask -TaskName $WatchdogTaskName -ErrorAction SilentlyContinue } catch {}
foreach ($watchdogProc in @(
    Get-CimInstance Win32_Process -ErrorAction SilentlyContinue |
        Where-Object {
            $_.CommandLine -and
            $_.CommandLine -like '*\.mhw-local-tools\heaven-bridge-watchdog.ps1*'
        }
)) {
    try { Stop-Process -Id ([int]$watchdogProc.ProcessId) -Force -ErrorAction Stop } catch {}
}
Start-Sleep -Milliseconds 500

# worker.py now owns a process-lifetime singleton lock. Therefore a bootstrap
# cannot verify a second live candidate while the old worker remains running.
# The safe handoff is: static verification + backup, stop old worker(s), start
# exactly one replacement, and restore/restart the backup if the replacement
# does not survive its startup window.
$oldWorkers = @(Get-CimInstance Win32_Process |
    Where-Object {
        $_.CommandLine -and (
            $_.CommandLine -like '*\.mhw-local-tools\heaven-desktop-worker.py*' -or
            $_.CommandLine -like '*\heaven-bridge\worker.py*'
        )
    })
$oldWorkerIds = @($oldWorkers | ForEach-Object { [int]$_.ProcessId })

foreach ($oldPid in $oldWorkerIds) {
    try { Stop-Process -Id $oldPid -Force -ErrorAction Stop } catch {}
}
Start-Sleep -Seconds 1

$taskManaged = $false
$proc = $null

if ($taskInstalled) {
    try {
        Start-ScheduledTask -TaskName $TaskName -ErrorAction Stop
        Start-Sleep -Seconds 3
        $proc = Get-HeavenBridgeWorker
        if ($proc) {
            $taskManaged = $true
        }
    } catch {
        Write-Warning "Task Scheduler start failed; falling back to direct worker start. $($_.Exception.Message)"
    }
}

if (-not $proc) {
    $candidate = Start-Process -WindowStyle Hidden -FilePath $pythonw -ArgumentList ('"{0}"' -f $RuntimeWorker) -WorkingDirectory $env:USERPROFILE -PassThru
    Start-Sleep -Seconds 3
    $proc = Get-CimInstance Win32_Process |
        Where-Object { $_.ProcessId -eq $candidate.Id } |
        Select-Object -First 1
}

if (-not $proc) {
    $rollback = $null
    if (Test-Path $BackupWorker) {
        Copy-Item $BackupWorker $RuntimeWorker -Force
        try {
            $rollbackCandidate = Start-Process -WindowStyle Hidden -FilePath $pythonw -ArgumentList ('"{0}"' -f $RuntimeWorker) -WorkingDirectory $env:USERPROFILE -PassThru
            Start-Sleep -Seconds 3
            $rollback = Get-CimInstance Win32_Process |
                Where-Object { $_.ProcessId -eq $rollbackCandidate.Id } |
                Select-Object -First 1
        } catch {}
    }

    if ($rollback) {
        throw ('Replacement Heaven Local Bridge worker failed; backup worker was restored and restarted as pid {0}.' -f $rollback.ProcessId)
    }
    throw 'Replacement Heaven Local Bridge worker failed and backup restart also failed; Startup fallback remains configured.'
}

$watchdogManaged = $false
if ($watchdogTaskInstalled) {
    try {
        Start-ScheduledTask -TaskName $WatchdogTaskName -ErrorAction Stop
        Start-Sleep -Seconds 2
        if (Get-HeavenBridgeWatchdog) {
            $watchdogManaged = $true
        }
    } catch {
        Write-Warning "Watchdog Task Scheduler start failed; falling back to direct watchdog start. $($_.Exception.Message)"
    }
}
if (-not $watchdogManaged) {
    Start-Process -WindowStyle Hidden -FilePath $powershell -ArgumentList @(
        '-NoProfile',
        '-ExecutionPolicy', 'Bypass',
        '-File', ('"{0}"' -f $RuntimeWatchdog)
    ) -WorkingDirectory $env:USERPROFILE | Out-Null
    Start-Sleep -Seconds 2
}
$watchdogProc = Get-HeavenBridgeWatchdog
if (-not $watchdogProc) {
    throw 'Heaven Local Bridge watchdog failed to start through both Task Scheduler and direct fallback.'
}

Write-Output ('HEAVEN_BRIDGE_STARTED pid={0} worker={1} python={2} task_managed={3} task_run_level={4} watchdog_pid={5} watchdog_managed={6} watchdog_run_level={7}' -f $proc.ProcessId, $RuntimeWorker, $pythonw, $taskManaged, $taskRunLevel, $watchdogProc.ProcessId, $watchdogManaged, $watchdogRunLevel)
