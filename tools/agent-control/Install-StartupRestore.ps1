param(
    [string]$RepoRoot = $env:AGENT_CONTROL_REPO,
    [string]$TaskName = 'Heaven Setup Restore',
    [string]$WatchdogTaskName = 'Heaven Agent Control Watchdog',
    [switch]$AllowNonControllerHost
)

$ErrorActionPreference = 'Stop'

if (-not $AllowNonControllerHost -and $env:COMPUTERNAME -and $env:COMPUTERNAME -ine 'heaven2') {
    throw "Startup restore must be installed on heaven2. Current host: $env:COMPUTERNAME"
}

if ([string]::IsNullOrWhiteSpace($RepoRoot)) {
    $RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
} else {
    $RepoRoot = (Resolve-Path $RepoRoot).Path
}

$restoreScript = Join-Path $RepoRoot 'tools\agent-control\Restore-StartupSetup.ps1'
$watchdogScript = Join-Path $RepoRoot 'tools\agent-control\Watch-AgentControl.ps1'
$sourceSyncScript = Join-Path $RepoRoot 'tools\agent-control\Sync-AgentControlRuntime.ps1'
if (-not (Test-Path -LiteralPath $restoreScript)) {
    throw "Startup restore script not found: $restoreScript"
}
if (-not (Test-Path -LiteralPath $watchdogScript)) {
    throw "Agent Control watchdog script not found: $watchdogScript"
}
if (-not (Test-Path -LiteralPath $sourceSyncScript)) {
    throw "Agent Control runtime freshness guard not found: $sourceSyncScript"
}

$appDir = Join-Path $env:LOCALAPPDATA 'MHW-Agent-Control'
New-Item -ItemType Directory -Force -Path $appDir | Out-Null

$runtimeSyncScript = Join-Path $appDir 'Sync-AgentControlRuntime.ps1'
Copy-Item -LiteralPath $sourceSyncScript -Destination $runtimeSyncScript -Force

$runtimeLauncher = Join-Path $appDir 'Run-StartupRestore.ps1'
$escapedRepo = $RepoRoot.Replace("'", "''")
$escapedRestore = $restoreScript.Replace("'", "''")
$escapedRuntimeSync = $runtimeSyncScript.Replace("'", "''")
$launcherText = @'
$ErrorActionPreference = 'Stop'
$env:AGENT_CONTROL_REPO = '__REPO__'
$runtime = & '__SYNC__' -RepoRoot '__REPO__'
$env:AGENT_CONTROL_SOURCE_SHA = [string]$runtime.source_sha
$env:AGENT_CONTROL_VERSION = [string]$runtime.agent_control_version
& '__RESTORE__' -RepoRoot '__REPO__' -ExpectedSourceSha $env:AGENT_CONTROL_SOURCE_SHA -ExpectedAgentControlVersion $env:AGENT_CONTROL_VERSION
'@
$launcherText = $launcherText.Replace('__REPO__', $escapedRepo).Replace('__RESTORE__', $escapedRestore).Replace('__SYNC__', $escapedRuntimeSync)
Set-Content -LiteralPath $runtimeLauncher -Value $launcherText -Encoding UTF8

$watchdogRuntimeLauncher = Join-Path $appDir 'Run-AgentControlWatchdog.ps1'
$escapedWatchdog = $watchdogScript.Replace("'", "''")
$watchdogLauncherText = @'
$ErrorActionPreference = 'Stop'
$env:AGENT_CONTROL_REPO = '__REPO__'
$runtime = & '__SYNC__' -RepoRoot '__REPO__'
$env:AGENT_CONTROL_SOURCE_SHA = [string]$runtime.source_sha
$env:AGENT_CONTROL_VERSION = [string]$runtime.agent_control_version
& '__WATCHDOG__' -RepoRoot '__REPO__' -ExpectedSourceSha $env:AGENT_CONTROL_SOURCE_SHA -ExpectedAgentControlVersion $env:AGENT_CONTROL_VERSION
'@
$watchdogLauncherText = $watchdogLauncherText.Replace('__REPO__', $escapedRepo).Replace('__WATCHDOG__', $escapedWatchdog).Replace('__SYNC__', $escapedRuntimeSync)
Set-Content -LiteralPath $watchdogRuntimeLauncher -Value $watchdogLauncherText -Encoding UTF8

$powerShellExe = "$env:SystemRoot\System32\WindowsPowerShell\v1.0\powershell.exe"
$arguments = '-NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File "' + $runtimeLauncher + '"'
$watchdogArguments = '-NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File "' + $watchdogRuntimeLauncher + '"'
$taskInstalled = $false
$taskError = $null
$watchdogTaskInstalled = $false
$watchdogTaskError = $null

try {
    if (Get-Command Register-ScheduledTask -ErrorAction SilentlyContinue) {
        $userId = [System.Security.Principal.WindowsIdentity]::GetCurrent().Name
        $action = New-ScheduledTaskAction -Execute $powerShellExe -Argument $arguments
        $trigger = New-ScheduledTaskTrigger -AtLogOn -User $userId
        $settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -StartWhenAvailable
        $principal = New-ScheduledTaskPrincipal -UserId $userId -LogonType Interactive -RunLevel Limited
        Register-ScheduledTask -TaskName $TaskName -Action $action -Trigger $trigger -Settings $settings -Principal $principal -Force | Out-Null
        $taskInstalled = [bool](Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue)
    }
} catch {
    $taskError = $_.Exception.Message
}

try {
    if (Get-Command Register-ScheduledTask -ErrorAction SilentlyContinue) {
        $userId = [System.Security.Principal.WindowsIdentity]::GetCurrent().Name
        $watchdogAction = New-ScheduledTaskAction -Execute $powerShellExe -Argument $watchdogArguments
        $watchdogTrigger = New-ScheduledTaskTrigger -AtLogOn -User $userId
        $watchdogSettings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -StartWhenAvailable -MultipleInstances IgnoreNew -RestartCount 255 -RestartInterval (New-TimeSpan -Minutes 1) -ExecutionTimeLimit ([TimeSpan]::Zero)
        $watchdogPrincipal = New-ScheduledTaskPrincipal -UserId $userId -LogonType Interactive -RunLevel Limited
        Register-ScheduledTask -TaskName $WatchdogTaskName -Action $watchdogAction -Trigger $watchdogTrigger -Settings $watchdogSettings -Principal $watchdogPrincipal -Force | Out-Null
        $watchdogTaskInstalled = [bool](Get-ScheduledTask -TaskName $WatchdogTaskName -ErrorAction SilentlyContinue)
        if ($watchdogTaskInstalled) {
            Stop-ScheduledTask -TaskName $WatchdogTaskName -ErrorAction SilentlyContinue
            Start-Sleep -Milliseconds 250
            Start-ScheduledTask -TaskName $WatchdogTaskName -ErrorAction SilentlyContinue
        }
    }
} catch {
    $watchdogTaskError = $_.Exception.Message
}

$startupDir = [Environment]::GetFolderPath('Startup')
$startupFallback = $null
$watchdogFallback = $null
if (-not [string]::IsNullOrWhiteSpace($startupDir)) {
    New-Item -ItemType Directory -Force -Path $startupDir | Out-Null
    $startupFallback = Join-Path $startupDir 'HeavenSetupRestore.vbs'
    $vbsText = @"
Set shell = CreateObject("WScript.Shell")
shell.Run """$powerShellExe"" -NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File ""$runtimeLauncher""", 0, False
"@
    Set-Content -LiteralPath $startupFallback -Value $vbsText -Encoding ASCII

    $watchdogFallback = Join-Path $startupDir 'HeavenAgentControlWatchdog.vbs'
    $watchdogVbsText = @"
Set shell = CreateObject("WScript.Shell")
shell.Run """$powerShellExe"" -NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File ""$watchdogRuntimeLauncher""", 0, False
"@
    Set-Content -LiteralPath $watchdogFallback -Value $watchdogVbsText -Encoding ASCII
}

if (-not $taskInstalled -and -not ($startupFallback -and (Test-Path -LiteralPath $startupFallback))) {
    if ($taskError) {
        throw "Could not install startup restore task or fallback. Scheduled task error: $taskError"
    }
    throw 'Could not install startup restore task or fallback.'
}
if (-not $watchdogTaskInstalled -and -not ($watchdogFallback -and (Test-Path -LiteralPath $watchdogFallback))) {
    if ($watchdogTaskError) {
        throw "Could not install Agent Control watchdog task or fallback. Scheduled task error: $watchdogTaskError"
    }
    throw 'Could not install Agent Control watchdog task or fallback.'
}

[pscustomobject]@{
    installed = $true
    repo = $RepoRoot
    task_name = $TaskName
    scheduled_task_installed = $taskInstalled
    scheduled_task_error = $taskError
    startup_fallback = $startupFallback
    runtime_launcher = $runtimeLauncher
    restore_script = $restoreScript
    watchdog_task_name = $WatchdogTaskName
    watchdog_task_installed = $watchdogTaskInstalled
    watchdog_task_error = $watchdogTaskError
    watchdog_fallback = $watchdogFallback
    watchdog_runtime_launcher = $watchdogRuntimeLauncher
    watchdog_script = $watchdogScript
    runtime_sync_script = $runtimeSyncScript
    source_sync_script = $sourceSyncScript
} | ConvertTo-Json -Compress
