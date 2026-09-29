param(
    [Parameter(Mandatory = $true)]
    [string]$UserProfile,
    [Parameter(Mandatory = $true)]
    [string]$UserIdentity,
    [Parameter(Mandatory = $true)]
    [string]$Pythonw,
    [Parameter(Mandatory = $true)]
    [string]$PowerShellExe,
    [switch]$Once,
    [ValidateRange(15, 300)]
    [int]$IntervalSeconds = 60
)

$ErrorActionPreference = 'Stop'

$WorkerTaskName = 'Heaven Local Bridge'
$WatchdogTaskName = 'Heaven Local Bridge Watchdog'
$SentinelTaskName = 'Heaven Local Bridge Sentinel'
$RuntimeDir = Join-Path $UserProfile '.mhw-local-tools'
$RuntimeWorker = Join-Path $RuntimeDir 'heaven-desktop-worker.py'
$BackupWorker = Join-Path $RuntimeDir 'heaven-desktop-worker.py.bak'
$RuntimeWatchdog = Join-Path $RuntimeDir 'heaven-bridge-watchdog.ps1'
$RuntimeSentinel = Join-Path $RuntimeDir 'heaven-bridge-sentinel.ps1'
$SourceBridgeDir = Join-Path $UserProfile 'HeavenBridgeSource\heaven-bridge'
$SourceWorker = Join-Path $SourceBridgeDir 'worker.py'
$SourceWatchdog = Join-Path $SourceBridgeDir 'watchdog.ps1'
$StateDir = Join-Path $UserProfile 'HeavenBridge'
$SentinelLog = Join-Path $StateDir 'sentinel.log'
$StartupDir = Join-Path $UserProfile 'AppData\Roaming\Microsoft\Windows\Start Menu\Programs\Startup'
$StartupWatchdogVbs = Join-Path $StartupDir 'HeavenBridgeWatchdog.vbs'
$RepoRoot = Join-Path $UserProfile 'local-ai-workspaces\mhw-mods'
$UserKey = ($UserIdentity -replace '[^A-Za-z0-9_.-]', '_')
$MutexName = "Global\MHW.HeavenBridgeSentinel.$UserKey"

New-Item -ItemType Directory -Force -Path $StateDir,$RuntimeDir | Out-Null

function Write-SentinelLog {
    param([Parameter(Mandatory = $true)][string]$Message)
    try {
        $line = '{0} {1}' -f ([DateTimeOffset]::UtcNow.ToString('o')), $Message
        Add-Content -Path $SentinelLog -Value $line -Encoding UTF8
    } catch {}
}

function New-ResilientTaskSettings {
    $settingsParams = @{
        AllowStartIfOnBatteries = $true
        DontStopIfGoingOnBatteries = $true
        StartWhenAvailable = $true
        MultipleInstances = 'IgnoreNew'
        RestartCount = 255
        RestartInterval = (New-TimeSpan -Minutes 1)
        ExecutionTimeLimit = [TimeSpan]::Zero
    }
    New-ScheduledTaskSettingsSet @settingsParams
}

function Test-TaskAction {
    param(
        [Parameter(Mandatory = $true)]$Task,
        [Parameter(Mandatory = $true)][string]$Execute,
        [Parameter(Mandatory = $true)][string]$Arguments
    )
    $action = @($Task.Actions) | Select-Object -First 1
    if (-not $action) { return $false }
    return (
        [string]::Equals([string]$action.Execute, $Execute, [System.StringComparison]::OrdinalIgnoreCase) -and
        [string]$action.Arguments -eq $Arguments
    )
}

function Ensure-InteractiveTask {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][string]$Execute,
        [Parameter(Mandatory = $true)][string]$Arguments
    )

    $task = $null
    try { $task = Get-ScheduledTask -TaskName $Name -ErrorAction Stop } catch {}
    $needsRegistration = (
        -not $task -or
        [string]$task.Principal.RunLevel -ne 'Highest' -or
        -not (Test-TaskAction -Task $task -Execute $Execute -Arguments $Arguments)
    )

    if ($needsRegistration) {
        $action = New-ScheduledTaskAction -Execute $Execute -Argument $Arguments
        $trigger = New-ScheduledTaskTrigger -AtLogOn
        $principal = New-ScheduledTaskPrincipal -UserId $UserIdentity -LogonType Interactive -RunLevel Highest
        Register-ScheduledTask -TaskName $Name -Action $action -Trigger $trigger -Settings (New-ResilientTaskSettings) -Principal $principal -Force | Out-Null
        Write-SentinelLog "Re-registered interactive task '$Name'."
        $task = Get-ScheduledTask -TaskName $Name -ErrorAction Stop
    }

    if ([string]$task.State -eq 'Disabled') {
        Enable-ScheduledTask -TaskName $Name | Out-Null
        Write-SentinelLog "Re-enabled interactive task '$Name'."
    }
}

function Get-SentinelArguments {
    '-NoProfile -ExecutionPolicy Bypass -File "{0}" -UserProfile "{1}" -UserIdentity "{2}" -Pythonw "{3}" -PowerShellExe "{4}"' -f $RuntimeSentinel, $UserProfile, $UserIdentity, $Pythonw, $PowerShellExe
}

function Ensure-SentinelTask {
    $arguments = Get-SentinelArguments
    $task = $null
    try { $task = Get-ScheduledTask -TaskName $SentinelTaskName -ErrorAction Stop } catch {}
    $principalId = if ($task) { [string]$task.Principal.UserId } else { '' }
    $isSystem = ($principalId -match '(?i)(^|\\)SYSTEM$' -or $principalId -eq 'S-1-5-18')
    $needsRegistration = (
        -not $task -or
        -not $isSystem -or
        [string]$task.Principal.RunLevel -ne 'Highest' -or
        -not (Test-TaskAction -Task $task -Execute $PowerShellExe -Arguments $arguments)
    )

    if ($needsRegistration) {
        $action = New-ScheduledTaskAction -Execute $PowerShellExe -Argument $arguments
        $trigger = New-ScheduledTaskTrigger -AtStartup
        $principal = New-ScheduledTaskPrincipal -UserId 'SYSTEM' -LogonType ServiceAccount -RunLevel Highest
        Register-ScheduledTask -TaskName $SentinelTaskName -Action $action -Trigger $trigger -Settings (New-ResilientTaskSettings) -Principal $principal -Force | Out-Null
        Write-SentinelLog "Re-registered SYSTEM sentinel task '$SentinelTaskName'."
        $task = Get-ScheduledTask -TaskName $SentinelTaskName -ErrorAction Stop
    }

    if ([string]$task.State -eq 'Disabled') {
        Enable-ScheduledTask -TaskName $SentinelTaskName | Out-Null
        Write-SentinelLog "Re-enabled SYSTEM sentinel task '$SentinelTaskName'."
    }
}

function Ensure-RuntimeFiles {
    if (-not (Test-Path $RuntimeWorker)) {
        if (Test-Path $BackupWorker) {
            Copy-Item $BackupWorker $RuntimeWorker -Force
            Write-SentinelLog 'Restored missing runtime worker from known-good backup.'
        } elseif (Test-Path $SourceWorker) {
            $python = if ($Pythonw -match '(?i)pythonw\.exe$') { $Pythonw -replace '(?i)pythonw\.exe$', 'python.exe' } else { $Pythonw }
            if (Test-Path $python) {
                & $python -m py_compile $SourceWorker
                if ($LASTEXITCODE -eq 0) {
                    Copy-Item $SourceWorker $RuntimeWorker -Force
                    Write-SentinelLog 'Restored missing runtime worker from canonical local source mirror.'
                }
            }
        }
    }

    if (-not (Test-Path $RuntimeWatchdog) -and (Test-Path $SourceWatchdog)) {
        $tokens = $null
        $errors = $null
        [void][System.Management.Automation.Language.Parser]::ParseFile((Resolve-Path $SourceWatchdog), [ref]$tokens, [ref]$errors)
        if ($errors.Count -eq 0) {
            Copy-Item $SourceWatchdog $RuntimeWatchdog -Force
            Write-SentinelLog 'Restored missing runtime watchdog from canonical local source mirror.'
        }
    }
}

function Ensure-StartupFallback {
    if (-not (Test-Path $RuntimeWatchdog)) { return }
    New-Item -ItemType Directory -Force -Path $StartupDir | Out-Null
    $escapedPowerShell = $PowerShellExe.Replace('"', '""')
    $escapedWatchdog = $RuntimeWatchdog.Replace('"', '""')
    $expected = @"
Set sh = CreateObject("WScript.Shell")
sh.Run """$escapedPowerShell"" -NoProfile -ExecutionPolicy Bypass -File ""$escapedWatchdog"" -StartupFallback", 0, False
"@
    $current = if (Test-Path $StartupWatchdogVbs) { Get-Content -Raw -Path $StartupWatchdogVbs } else { $null }
    if ($current -ne $expected) {
        Set-Content -Path $StartupWatchdogVbs -Value $expected -Encoding ASCII
        Write-SentinelLog 'Repaired Startup-folder watchdog fallback.'
    }
}

function Get-TargetDesktop {
    try {
        $sid = (New-Object System.Security.Principal.NTAccount($UserIdentity)).Translate([System.Security.Principal.SecurityIdentifier]).Value
        $shellFolders = "Registry::HKEY_USERS\$sid\Software\Microsoft\Windows\CurrentVersion\Explorer\Shell Folders"
        $desktop = (Get-ItemProperty -Path $shellFolders -Name Desktop -ErrorAction Stop).Desktop
        if ($desktop -and (Test-Path $desktop)) { return [string]$desktop }
    } catch {}

    $fallback = Join-Path $UserProfile 'Desktop'
    if (Test-Path $fallback) { return $fallback }
    return $null
}

function Ensure-AgentControlRecoveryShortcut {
    if (-not $env:COMPUTERNAME -or $env:COMPUTERNAME -ine 'heaven2') { return }
    $agentDir = Join-Path $RepoRoot 'tools\agent-control'
    $launcher = Join-Path $agentDir 'Start Agent Control.bat'
    if (-not (Test-Path $launcher)) { return }

    $desktop = Get-TargetDesktop
    if (-not $desktop) { return }
    $shortcutPath = Join-Path $desktop 'Heaven Agent Control.lnk'
    if (Test-Path $shortcutPath) { return }

    $wsh = New-Object -ComObject WScript.Shell
    $shortcut = $wsh.CreateShortcut($shortcutPath)
    $shortcut.TargetPath = $env:ComSpec
    $shortcut.Arguments = '/c ""{0}""' -f $launcher
    $shortcut.WorkingDirectory = $agentDir
    $shortcut.Description = 'Open Heaven Agent Control'
    $customIcon = Join-Path $UserProfile 'AppData\Local\MHW-Agent-Control\agent-control.ico'
    if (Test-Path $customIcon) { $shortcut.IconLocation = "$customIcon,0" } else { $shortcut.IconLocation = "$PowerShellExe,0" }
    $shortcut.Save()
    Write-SentinelLog "Restored missing Heaven Agent Control shortcut at '$shortcutPath'."
}

function Invoke-SentinelRepair {
    Ensure-RuntimeFiles
    if (-not (Test-Path $RuntimeWorker) -or -not (Test-Path $RuntimeWatchdog)) {
        Write-SentinelLog 'Runtime worker/watchdog is unavailable; preserving remaining recovery owners for the next bootstrap.'
        return
    }

    Ensure-StartupFallback
    $workerArgs = '"{0}"' -f $RuntimeWorker
    $watchdogArgs = '-NoProfile -ExecutionPolicy Bypass -File "{0}"' -f $RuntimeWatchdog
    Ensure-InteractiveTask -Name $WorkerTaskName -Execute $Pythonw -Arguments $workerArgs
    Ensure-InteractiveTask -Name $WatchdogTaskName -Execute $PowerShellExe -Arguments $watchdogArgs
    Ensure-SentinelTask
    Ensure-AgentControlRecoveryShortcut

    try { Start-ScheduledTask -TaskName $WatchdogTaskName -ErrorAction Stop } catch {
        Write-SentinelLog ("Watchdog task start request failed: {0}" -f $_.Exception.Message)
    }
}

$mutex = New-Object System.Threading.Mutex($false, $MutexName)
$acquired = $false
try {
    try { $acquired = $mutex.WaitOne(0) } catch [System.Threading.AbandonedMutexException] { $acquired = $true }
    if (-not $acquired) {
        Write-SentinelLog 'Another sentinel instance owns the singleton mutex; exiting.'
        exit 0
    }

    Write-SentinelLog ("SYSTEM sentinel started once={0} interval={1}s user={2}" -f $Once, $IntervalSeconds, $UserIdentity)
    do {
        try { Invoke-SentinelRepair } catch { Write-SentinelLog ("Sentinel repair cycle failed: {0}" -f $_.Exception.Message) }
        if ($Once) { break }
        Start-Sleep -Seconds $IntervalSeconds
    } while ($true)
} finally {
    if ($acquired) { try { $mutex.ReleaseMutex() } catch {} }
    $mutex.Dispose()
}
