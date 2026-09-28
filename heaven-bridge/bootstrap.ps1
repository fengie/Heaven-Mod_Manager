[CmdletBinding()]
param([switch]$NoRestart)

$ErrorActionPreference = 'Stop'

$RepoRoot = Join-Path $env:USERPROFILE 'HeavenBridgeRepo'
$RepoUrl = 'https://github.com/fengie/mhw-mods.git'
$Branch = 'heaven-bridge'
$RuntimeDir = Join-Path $env:USERPROFILE '.mhw-local-tools'
$RuntimeWorker = Join-Path $RuntimeDir 'heaven-desktop-worker.py'
$SourceWorker = Join-Path $RepoRoot 'heaven-bridge\worker.py'
$StartupDir = [Environment]::GetFolderPath('Startup')
$StartupLauncher = Join-Path $StartupDir 'HeavenBridgeWorker.cmd'
$TaskName = 'HeavenLocalBridge'

function Invoke-GitRetry {
    param(
        [Parameter(Mandatory=$true)][string[]]$GitArgs,
        [int]$Attempts = 5
    )
    for ($i = 0; $i -lt $Attempts; $i++) {
        & git @GitArgs
        if ($LASTEXITCODE -eq 0) { return }
        Start-Sleep -Seconds ([Math]::Min(20, [Math]::Pow(2, $i)))
    }
    throw "git failed after $Attempts attempts: git $($GitArgs -join ' ')"
}

if (-not (Test-Path (Join-Path $RepoRoot '.git'))) {
    Invoke-GitRetry -GitArgs @('clone','--branch',$Branch,'--single-branch',$RepoUrl,$RepoRoot)
} else {
    Invoke-GitRetry -GitArgs @('-C',$RepoRoot,'fetch','origin',$Branch)
    & git -C $RepoRoot checkout $Branch
    if ($LASTEXITCODE -ne 0) {
        & git -C $RepoRoot checkout -B $Branch "origin/$Branch"
    }
    Invoke-GitRetry -GitArgs @('-C',$RepoRoot,'pull','--rebase','origin',$Branch)
}

New-Item -ItemType Directory -Force -Path $RuntimeDir | Out-Null
python.exe -m py_compile $SourceWorker
if ($LASTEXITCODE -ne 0) { throw 'Worker syntax validation failed; existing runtime worker was not replaced.' }

$TempWorker = "$RuntimeWorker.new"
Copy-Item -LiteralPath $SourceWorker -Destination $TempWorker -Force
Move-Item -LiteralPath $TempWorker -Destination $RuntimeWorker -Force
python.exe -m py_compile $RuntimeWorker
if ($LASTEXITCODE -ne 0) { throw 'Runtime worker syntax validation failed.' }

$launcherBody = @"
@echo off
cd /d "%USERPROFILE%\HeavenBridgeRepo"
start "" /min pythonw.exe "%USERPROFILE%\.mhw-local-tools\heaven-desktop-worker.py"
"@
Set-Content -Path $StartupLauncher -Value $launcherBody -Encoding ASCII

# Best-effort Scheduled Task gives automatic recovery after login. Startup fallback remains installed.
try {
    $action = New-ScheduledTaskAction -Execute 'pythonw.exe' -Argument ('"' + $RuntimeWorker + '"') -WorkingDirectory $RepoRoot
    $trigger = New-ScheduledTaskTrigger -AtLogOn -User $env:USERNAME
    $settings = New-ScheduledTaskSettingsSet -RestartCount 6 -RestartInterval (New-TimeSpan -Minutes 1) -ExecutionTimeLimit (New-TimeSpan -Days 30) -MultipleInstances IgnoreNew
    Register-ScheduledTask -TaskName $TaskName -Action $action -Trigger $trigger -Settings $settings -Description 'Heaven Local Bridge worker' -Force | Out-Null
} catch {
    Write-Warning "Scheduled Task install failed; Startup launcher remains available: $($_.Exception.Message)"
}

if ($NoRestart) {
    Write-Output "HEAVEN_BRIDGE_INSTALLED_NO_RESTART runtime=$RuntimeWorker"
    exit 0
}

# Replace only bridge-worker processes, not unrelated Python processes.
Get-CimInstance Win32_Process |
    Where-Object { $_.CommandLine -like '*heaven-desktop-worker.py*' -or $_.CommandLine -like '*heaven-bridge\worker.py*' } |
    ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }

Start-Process -WindowStyle Hidden -FilePath 'pythonw.exe' -ArgumentList ('"' + $RuntimeWorker + '"') -WorkingDirectory $RepoRoot
Start-Sleep -Seconds 2

$running = Get-CimInstance Win32_Process | Where-Object { $_.CommandLine -like '*heaven-desktop-worker.py*' }
if (-not $running) { throw 'Heaven Local Bridge worker did not remain running after startup.' }

Write-Output "HEAVEN_BRIDGE_STARTED runtime=$RuntimeWorker pid=$($running.ProcessId -join ',')"
