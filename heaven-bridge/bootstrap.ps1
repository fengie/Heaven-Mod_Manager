$ErrorActionPreference = 'Stop'

$RepoRoot = Join-Path $env:USERPROFILE 'HeavenBridgeRepo'
$RepoUrl = 'https://github.com/fengie/mhw-mods.git'
$Branch = 'heaven-bridge'
$RuntimeDir = Join-Path $env:USERPROFILE '.mhw-local-tools'
$RuntimeWorker = Join-Path $RuntimeDir 'heaven-desktop-worker.py'
$BackupWorker = Join-Path $RuntimeDir 'heaven-desktop-worker.py.bak'
$SourceWorker = Join-Path $RepoRoot 'heaven-bridge\worker.py'
$Startup = [Environment]::GetFolderPath('Startup')
$StartupVbs = Join-Path $Startup 'HeavenBridgeWorker.vbs'
$TaskName = 'Heaven Local Bridge'

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

    $currentBranch = (& git -C $RepoRoot branch --show-current).Trim()
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

$python = (Get-Command python.exe -ErrorAction Stop).Source
$pythonw = (Get-Command pythonw.exe -ErrorAction Stop).Source

$staged = Join-Path $RuntimeDir 'heaven-desktop-worker.py.new'
Copy-Item $SourceWorker $staged -Force

& $python -m py_compile $staged
if ($LASTEXITCODE -ne 0) {
    Remove-Item $staged -Force -ErrorAction SilentlyContinue
    throw 'Worker syntax validation failed.'
}

if (Test-Path $RuntimeWorker) {
    Copy-Item $RuntimeWorker $BackupWorker -Force
}
Move-Item $staged $RuntimeWorker -Force

# Startup-folder fallback. Use an absolute pythonw path so a reduced logon PATH cannot break startup.
$escapedPythonw = $pythonw.Replace('"', '""')
$escapedWorker = $RuntimeWorker.Replace('"', '""')
$vbs = @"
Set sh = CreateObject("WScript.Shell")
sh.Run """$escapedPythonw"" ""$escapedWorker""", 0, False
"@
Set-Content -Path $StartupVbs -Value $vbs -Encoding ASCII

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

# Prefer Task Scheduler because it supports restart-on-failure. The canonical task
# runs in the currently logged-in user's interactive session at RunLevel Highest.
# This preserves GUI/desktop access while giving bridge commands the user's full
# elevated administrator token. Windows still enforces the one-time security
# boundary when creating/upgrading this task; after it exists, normal bridge
# jobs do not need per-command UAC elevation.
$taskInstalled = $false
$taskRunLevel = 'Unavailable'
$existingTask = $null
try {
    $existingTask = Get-ScheduledTask -TaskName $TaskName -ErrorAction Stop
    $taskRunLevel = [string]$existingTask.Principal.RunLevel
} catch {}

if ($existingTask -and $taskRunLevel -eq 'Highest') {
    # Keep an already-elevated task. Its action points at the stable runtime path,
    # which bootstrap refreshes above on every update.
    $taskInstalled = $true
} else {
    try {
        $identityName = [System.Security.Principal.WindowsIdentity]::GetCurrent().Name
        if ([string]::IsNullOrWhiteSpace($identityName)) {
            throw 'Unable to resolve the current Windows identity for the scheduled-task principal.'
        }

        $action = New-ScheduledTaskAction -Execute $pythonw -Argument ('"{0}"' -f $RuntimeWorker)
        $trigger = New-ScheduledTaskTrigger -AtLogOn
        $settings = New-ScheduledTaskSettingsSet `
            -AllowStartIfOnBatteries `
            -DontStopIfGoingOnBatteries `
            -StartWhenAvailable `
            -MultipleInstances IgnoreNew `
            -RestartCount 12 `
            -RestartInterval (New-TimeSpan -Minutes 1)
        $principal = New-ScheduledTaskPrincipal `
            -UserId $identityName `
            -LogonType Interactive `
            -RunLevel Highest

        Register-ScheduledTask `
            -TaskName $TaskName `
            -Action $action `
            -Trigger $trigger `
            -Settings $settings `
            -Principal $principal `
            -Force | Out-Null

        $taskRunLevel = [string](Get-ScheduledTask -TaskName $TaskName -ErrorAction Stop).Principal.RunLevel
        if ($taskRunLevel -ne 'Highest') {
            throw "Scheduled task registered but reports unexpected RunLevel '$taskRunLevel'."
        }
        $taskInstalled = $true
    } catch {
        Write-Warning "Highest-privilege Scheduled Task install/upgrade failed; Startup VBS remains configured. A one-time elevated registration is required before the bridge can hold an administrator token. $($_.Exception.Message)"
    }
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

Write-Output ('HEAVEN_BRIDGE_STARTED pid={0} worker={1} python={2} task_managed={3} task_run_level={4}' -f $proc.ProcessId, $RuntimeWorker, $pythonw, $taskManaged, $taskRunLevel)
