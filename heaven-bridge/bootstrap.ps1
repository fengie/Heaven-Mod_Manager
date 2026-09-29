$ErrorActionPreference = 'Stop'

$RepoRoot = Join-Path $env:USERPROFILE 'HeavenBridgeRepo'
$SourceRepoRoot = Join-Path $env:USERPROFILE 'HeavenBridgeSource'
$RepoUrl = 'https://github.com/fengie/mhw-mods.git'
$Branch = 'heaven-bridge'
$SourceBranch = 'main'
$RuntimeDir = Join-Path $env:USERPROFILE '.mhw-local-tools'
$RuntimeWorker = Join-Path $RuntimeDir 'heaven-desktop-worker.py'
$BackupWorker = Join-Path $RuntimeDir 'heaven-desktop-worker.py.bak'
$RuntimeWatchdog = Join-Path $RuntimeDir 'heaven-bridge-watchdog.ps1'
$BackupWatchdog = Join-Path $RuntimeDir 'heaven-bridge-watchdog.ps1.bak'
$RuntimeSentinel = Join-Path $RuntimeDir 'heaven-bridge-sentinel.ps1'
$BackupSentinel = Join-Path $RuntimeDir 'heaven-bridge-sentinel.ps1.bak'
$SourceWorker = Join-Path $SourceRepoRoot 'heaven-bridge\worker.py'
$SourceWatchdog = Join-Path $SourceRepoRoot 'heaven-bridge\watchdog.ps1'
$SourceSentinel = Join-Path $SourceRepoRoot 'heaven-bridge\sentinel.ps1'
$SourceTestWorker = Join-Path $SourceRepoRoot 'heaven-bridge\test_worker.py'
$SourceBootstrap = Join-Path $SourceRepoRoot 'heaven-bridge\bootstrap.ps1'
$Startup = [Environment]::GetFolderPath('Startup')
$StartupVbs = Join-Path $Startup 'HeavenBridgeWorker.vbs'
$StartupWatchdogVbs = Join-Path $Startup 'HeavenBridgeWatchdog.vbs'
$TaskName = 'Heaven Local Bridge'
$WatchdogTaskName = 'Heaven Local Bridge Watchdog'
$SentinelTaskName = 'Heaven Local Bridge Sentinel'

# Keep the recovery entrypoint self-contained. The relay/bootstrap copy must be
# able to refresh and hand off to canonical main even when neighboring relay
# files are missing or only partially synchronized.
function Get-HeavenBridgeGitCurrentBranch {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$Repository
    )

    $branchOutput = @(& git -C $Repository branch --show-current)
    if ($LASTEXITCODE -ne 0) {
        throw "Unable to read Git branch for Heaven Bridge repository: $Repository"
    }
    if ($branchOutput.Count -eq 0) {
        return $null
    }

    $branch = (($branchOutput | ForEach-Object { [string]$_ }) -join "`n").Trim()
    if ([string]::IsNullOrWhiteSpace($branch)) {
        return $null
    }
    return $branch
}

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

function Invoke-SourceGitChecked {
    param([Parameter(Mandatory = $true)][string[]]$GitArgs)
    & git -C $SourceRepoRoot @GitArgs
    if ($LASTEXITCODE -ne 0) {
        throw ('source git {0} failed with exit code {1}' -f ($GitArgs -join ' '), $LASTEXITCODE)
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

# Keep runtime source independent from the operational relay checkout. The relay
# branch is queue/status/results transport and may intentionally diverge from
# canonical development history. Runtime binaries always come from a disposable
# main-branch source mirror when GitHub is reachable; if refresh is temporarily
# unavailable, bootstrap may use the last-known-good mirror already on disk.
$sourceRefreshSucceeded = $false
if (-not (Test-Path (Join-Path $SourceRepoRoot '.git'))) {
    git clone --branch $SourceBranch --single-branch $RepoUrl $SourceRepoRoot
    if ($LASTEXITCODE -ne 0) {
        throw "canonical source clone failed with exit code $LASTEXITCODE"
    }
    $sourceRefreshSucceeded = $true
} else {
    $sourceCurrentBranch = Get-HeavenBridgeGitCurrentBranch -Repository $SourceRepoRoot
    $sourceTrackedDirty = @(& git -C $SourceRepoRoot status --porcelain --untracked-files=no)
    if ($LASTEXITCODE -ne 0) { throw 'Unable to inspect canonical source mirror.' }
    $sourceFallbackEligible = (
        $sourceCurrentBranch -eq $SourceBranch -and
        $sourceTrackedDirty.Count -eq 0
    )

    try {
        Invoke-SourceGitChecked @('fetch', 'origin', $SourceBranch)
        Invoke-SourceGitChecked @('checkout', '-B', $SourceBranch, "origin/$SourceBranch")
        Invoke-SourceGitChecked @('reset', '--hard', "origin/$SourceBranch")
        # This checkout is explicitly disposable. Remove stale untracked bridge
        # source/test files so they cannot shadow canonical tracked content.
        Invoke-SourceGitChecked @('clean', '-fd', '--', 'heaven-bridge')
        $sourceRefreshSucceeded = $true
    } catch {
        if (-not $sourceFallbackEligible) {
            throw ("Canonical main source refresh failed and the local source mirror is not a clean main checkout. {0}" -f $_.Exception.Message)
        }
        Write-Warning ("Canonical main source refresh failed; using clean last-known-good main source mirror. {0}" -f $_.Exception.Message)
    }
}

if (-not (Test-Path $SourceWorker)) {
    throw "Bridge worker source missing from canonical source mirror: $SourceWorker"
}
if (-not (Test-Path $SourceWatchdog)) {
    throw "Bridge watchdog source missing from canonical source mirror: $SourceWatchdog"
}
if (-not (Test-Path $SourceSentinel)) {
    throw "Bridge sentinel source missing from canonical source mirror: $SourceSentinel"
}
if (-not (Test-Path $SourceTestWorker)) {
    throw "Bridge regression suite missing from canonical source mirror: $SourceTestWorker"
}
if (-not (Test-Path $SourceBootstrap)) {
    throw "Bridge bootstrap missing from canonical source mirror: $SourceBootstrap"
}

$SourceRevision = (& git -C $SourceRepoRoot rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($SourceRevision)) {
    throw 'Unable to resolve canonical bridge source revision.'
}
Write-Output ("HEAVEN_BRIDGE_SOURCE revision={0} refreshed={1} branch={2}" -f $SourceRevision, $sourceRefreshSucceeded, $SourceBranch)

# A relay/runtime copy of bootstrap is only a stable entrypoint. After it has
# refreshed the canonical source mirror, hand execution to main's bootstrap so
# future bootstrap fixes do not require manual transport-branch synchronization.
$currentBootstrap = [System.IO.Path]::GetFullPath($PSCommandPath)
$canonicalBootstrap = [System.IO.Path]::GetFullPath((Resolve-Path $SourceBootstrap).Path)
if ($currentBootstrap -ne $canonicalBootstrap) {
    Write-Output ("HEAVEN_BRIDGE_BOOTSTRAP_HANDOFF source={0}" -f $canonicalBootstrap)
    & $SourceBootstrap
    if (-not $?) {
        throw 'Canonical Heaven Bridge bootstrap handoff failed.'
    }
    exit 0
}

$python = (Get-Command python.exe -ErrorAction Stop).Source
$pythonw = (Get-Command pythonw.exe -ErrorAction Stop).Source
$powershell = (Get-Command powershell.exe -ErrorAction Stop).Source

foreach ($scriptToValidate in @($SourceWatchdog, $SourceSentinel)) {
    $scriptTokens = $null
    $scriptErrors = $null
    [void][System.Management.Automation.Language.Parser]::ParseFile(
        (Resolve-Path $scriptToValidate),
        [ref]$scriptTokens,
        [ref]$scriptErrors
    )
    if ($scriptErrors.Count -gt 0) {
        $scriptErrors | Format-List | Out-String | Write-Error
        throw "Bridge recovery script syntax validation failed: $scriptToValidate"
    }
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
Push-Location $SourceRepoRoot
try {
    & $python -m unittest -q 'heaven-bridge\test_worker.py'
    if ($LASTEXITCODE -ne 0) {
        throw "Worker regression suite failed with exit code $LASTEXITCODE."
    }
} finally {
    Pop-Location
}

# Publish the validated runtime only after the full regression suite passes.
if (Test-Path $RuntimeWorker) {
    Copy-Item $RuntimeWorker $BackupWorker -Force
}
if (Test-Path $RuntimeWatchdog) {
    Copy-Item $RuntimeWatchdog $BackupWatchdog -Force
}
if (Test-Path $RuntimeSentinel) {
    Copy-Item $RuntimeSentinel $BackupSentinel -Force
}
Move-Item $staged $RuntimeWorker -Force
Copy-Item $SourceWatchdog $RuntimeWatchdog -Force
Copy-Item $SourceSentinel $RuntimeSentinel -Force

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
$sentinelTaskInstalled = $false
$sentinelRunLevel = 'Unavailable'

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

    # Machine-start repair owner in a separate principal/trigger failure domain.
    # It never runs bridge jobs; it only repairs persistence for the interactive
    # worker/watchdog and the user Startup fallback.
    $sentinelArguments = '-NoProfile -ExecutionPolicy Bypass -File "{0}" -UserProfile "{1}" -UserIdentity "{2}" -Pythonw "{3}" -PowerShellExe "{4}"' -f $RuntimeSentinel, $env:USERPROFILE, $identityName, $pythonw, $powershell
    $sentinelAction = New-ScheduledTaskAction -Execute $powershell -Argument $sentinelArguments
    $sentinelTrigger = New-ScheduledTaskTrigger -AtStartup
    $sentinelPrincipal = New-ScheduledTaskPrincipal -UserId 'SYSTEM' -LogonType ServiceAccount -RunLevel Highest
    Register-ScheduledTask `
        -TaskName $SentinelTaskName `
        -Action $sentinelAction `
        -Trigger $sentinelTrigger `
        -Settings $settings `
        -Principal $sentinelPrincipal `
        -Force | Out-Null

    $sentinelTask = Get-ScheduledTask -TaskName $SentinelTaskName -ErrorAction Stop
    $sentinelRunLevel = [string]$sentinelTask.Principal.RunLevel
    $sentinelPrincipalId = [string]$sentinelTask.Principal.UserId
    if ($sentinelRunLevel -ne 'Highest' -or $sentinelPrincipalId -notmatch '(?i)(^|\\)SYSTEM$|^S-1-5-18$') {
        throw "Sentinel task registered with unexpected principal/run level: principal='$sentinelPrincipalId' runLevel='$sentinelRunLevel'."
    }
    $sentinelTaskInstalled = $true
} catch {
    Write-Warning "Highest-privilege Scheduled Task install/upgrade failed; the Startup fallback remains configured, but worker/watchdog/sentinel task health is degraded until elevated registration succeeds. $($_.Exception.Message)"
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

function Get-HeavenBridgeSentinel {
    Get-CimInstance Win32_Process -ErrorAction SilentlyContinue |
        Where-Object {
            $_.CommandLine -and
            $_.CommandLine -like '*\.mhw-local-tools\heaven-bridge-sentinel.ps1*'
        } |
        Sort-Object CreationDate -Descending |
        Select-Object -First 1
}

# Stop the SYSTEM sentinel before an intentional worker/watchdog handoff so it
# cannot race bootstrap by repairing the tasks we are deliberately replacing.
try { Stop-ScheduledTask -TaskName $SentinelTaskName -ErrorAction SilentlyContinue } catch {}
foreach ($sentinelProc in @(
    Get-CimInstance Win32_Process -ErrorAction SilentlyContinue |
        Where-Object {
            $_.CommandLine -and
            $_.CommandLine -like '*\.mhw-local-tools\heaven-bridge-sentinel.ps1*'
        }
)) {
    try { Stop-Process -Id ([int]$sentinelProc.ProcessId) -Force -ErrorAction Stop } catch {}
}
Start-Sleep -Milliseconds 250

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

$sentinelProc = $null
if ($sentinelTaskInstalled) {
    try {
        Start-ScheduledTask -TaskName $SentinelTaskName -ErrorAction Stop
        Start-Sleep -Seconds 2
        $sentinelProc = Get-HeavenBridgeSentinel
    } catch {
        Write-Warning "SYSTEM sentinel start failed: $($_.Exception.Message)"
    }
}
if ($sentinelTaskInstalled -and -not $sentinelProc) {
    throw 'Heaven Local Bridge SYSTEM sentinel task is installed but its process did not start.'
}

$sentinelPid = if ($sentinelProc) { [string]$sentinelProc.ProcessId } else { 'unavailable' }
Write-Output ('HEAVEN_BRIDGE_STARTED pid={0} worker={1} python={2} task_managed={3} task_run_level={4} watchdog_pid={5} watchdog_managed={6} watchdog_run_level={7} sentinel_pid={8} sentinel_run_level={9}' -f $proc.ProcessId, $RuntimeWorker, $pythonw, $taskManaged, $taskRunLevel, $watchdogProc.ProcessId, $watchdogManaged, $watchdogRunLevel, $sentinelPid, $sentinelRunLevel)
