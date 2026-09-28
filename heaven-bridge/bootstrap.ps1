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

# Stop only prior Heaven Local Bridge Python workers. Do not touch unrelated Python processes.
Get-CimInstance Win32_Process |
    Where-Object {
        $_.CommandLine -and (
            $_.CommandLine -like '*\.mhw-local-tools\heaven-desktop-worker.py*' -or
            $_.CommandLine -like '*\heaven-bridge\worker.py*'
        )
    } |
    ForEach-Object {
        try { Stop-Process -Id $_.ProcessId -Force -ErrorAction Stop } catch {}
    }

Start-Process -WindowStyle Hidden -FilePath $pythonw -ArgumentList ('"{0}"' -f $RuntimeWorker) -WorkingDirectory $env:USERPROFILE
Start-Sleep -Seconds 2

$proc = Get-CimInstance Win32_Process |
    Where-Object { $_.CommandLine -and $_.CommandLine -like '*\.mhw-local-tools\heaven-desktop-worker.py*' } |
    Select-Object -First 1

if (-not $proc) {
    if (Test-Path $BackupWorker) {
        Copy-Item $BackupWorker $RuntimeWorker -Force
        Start-Process -WindowStyle Hidden -FilePath $pythonw -ArgumentList ('"{0}"' -f $RuntimeWorker) -WorkingDirectory $env:USERPROFILE
    }
    throw 'New Heaven Local Bridge worker did not remain running; backup restoration attempted.'
}

Write-Output ('HEAVEN_BRIDGE_STARTED pid={0} worker={1} python={2}' -f $proc.ProcessId, $RuntimeWorker, $pythonw)
