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
New-Item -ItemType Directory -Force -Path $RuntimeDir | Out-Null

if (-not (Test-Path (Join-Path $RepoRoot '.git'))) {
    git clone --branch $Branch --single-branch $RepoUrl $RepoRoot
} else {
    git -C $RepoRoot fetch origin $Branch
    git -C $RepoRoot checkout $Branch
    git -C $RepoRoot pull --rebase origin $Branch
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
foreach ($pid in $oldWorkerIds) {
    if ($pid -eq $candidate.Id) { continue }
    try { Stop-Process -Id $pid -Force -ErrorAction Stop } catch {}
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
