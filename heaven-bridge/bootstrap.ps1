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

# Prefer Task Scheduler because it supports restart-on-failure. This is user-level; fallback remains the Startup VBS.
try {
    $action = New-ScheduledTaskAction -Execute $pythonw -Argument ('"{0}"' -f $RuntimeWorker)
    $trigger = New-ScheduledTaskTrigger -AtLogOn
    $settings = New-ScheduledTaskSettingsSet `
        -AllowStartIfOnBatteries `
        -DontStopIfGoingOnBatteries `
        -StartWhenAvailable `
        -MultipleInstances IgnoreNew `
        -RestartCount 12 `
        -RestartInterval (New-TimeSpan -Minutes 1)
    Register-ScheduledTask -TaskName $TaskName -Action $action -Trigger $trigger -Settings $settings -Force | Out-Null
} catch {
    Write-Warning "Scheduled Task install failed; Startup VBS remains configured. $($_.Exception.Message)"
}

# Fail-safe handoff: start and verify the replacement before touching any
# existing bridge worker. A failed upgrade must never strand ChatGPT without
# its only authorized local-control path.
$oldWorkers = @(Get-CimInstance Win32_Process |
    Where-Object {
        $_.CommandLine -and (
            $_.CommandLine -like '*\.mhw-local-tools\heaven-desktop-worker.py*' -or
            $_.CommandLine -like '*\heaven-bridge\worker.py*'
        )
    })
$oldWorkerIds = @($oldWorkers | ForEach-Object { [int]$_.ProcessId })

$candidate = Start-Process -WindowStyle Hidden -FilePath $pythonw -ArgumentList ('"{0}"' -f $RuntimeWorker) -WorkingDirectory $env:USERPROFILE -PassThru
Start-Sleep -Seconds 3

$candidateAlive = Get-Process -Id $candidate.Id -ErrorAction SilentlyContinue
if (-not $candidateAlive) {
    if (Test-Path $BackupWorker) {
        Copy-Item $BackupWorker $RuntimeWorker -Force
    }
    throw 'Replacement Heaven Local Bridge worker failed before handoff; existing worker(s) were left untouched.'
}

# The verified candidate now covers the handoff window. Move steady-state
# ownership to Task Scheduler when available so restart-on-failure supervises
# the running worker rather than only a future logon instance.
$taskManaged = $false
try {
    Stop-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
    Start-ScheduledTask -TaskName $TaskName -ErrorAction Stop
    Start-Sleep -Seconds 3

    $taskWorker = Get-CimInstance Win32_Process |
        Where-Object {
            $_.ProcessId -ne $candidate.Id -and
            $_.CommandLine -and
            $_.CommandLine -like '*\.mhw-local-tools\heaven-desktop-worker.py*'
        } |
        Sort-Object CreationDate -Descending |
        Select-Object -First 1

    if ($taskWorker) {
        $taskManaged = $true
    }
} catch {
    Write-Warning "Task Scheduler handoff failed; verified candidate remains running. $($_.Exception.Message)"
}

# Retire only pre-upgrade workers after a replacement has already survived.
foreach ($oldPid in $oldWorkerIds) {
    if ($oldPid -eq $candidate.Id) { continue }
    try { Stop-Process -Id $oldPid -Force -ErrorAction Stop } catch {}
}

if ($taskManaged) {
    try { Stop-Process -Id $candidate.Id -Force -ErrorAction Stop } catch {}
    $proc = $taskWorker
} else {
    $proc = Get-CimInstance Win32_Process |
        Where-Object { $_.ProcessId -eq $candidate.Id } |
        Select-Object -First 1
}

if (-not $proc) {
    throw 'Verified replacement disappeared during handoff. Startup fallback remains configured; manual bootstrap may be required.'
}

Write-Output ('HEAVEN_BRIDGE_STARTED pid={0} worker={1} python={2} task_managed={3}' -f $proc.ProcessId, $RuntimeWorker, $pythonw, $taskManaged)
