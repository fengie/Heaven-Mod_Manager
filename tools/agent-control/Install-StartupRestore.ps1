param(
    [string]$RepoRoot = $env:AGENT_CONTROL_REPO,
    [string]$TaskName = 'Heaven Setup Restore',
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
if (-not (Test-Path -LiteralPath $restoreScript)) {
    throw "Startup restore script not found: $restoreScript"
}

$appDir = Join-Path $env:LOCALAPPDATA 'MHW-Agent-Control'
New-Item -ItemType Directory -Force -Path $appDir | Out-Null

$runtimeLauncher = Join-Path $appDir 'Run-StartupRestore.ps1'
$escapedRepo = $RepoRoot.Replace("'", "''")
$escapedRestore = $restoreScript.Replace("'", "''")
$launcherText = @'
$ErrorActionPreference = 'Stop'
$env:AGENT_CONTROL_REPO = '__REPO__'
& '__RESTORE__' -RepoRoot '__REPO__'
'@
$launcherText = $launcherText.Replace('__REPO__', $escapedRepo).Replace('__RESTORE__', $escapedRestore)
Set-Content -LiteralPath $runtimeLauncher -Value $launcherText -Encoding UTF8

$powerShellExe = "$env:SystemRoot\System32\WindowsPowerShell\v1.0\powershell.exe"
$arguments = '-NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File "' + $runtimeLauncher + '"'
$taskInstalled = $false
$taskError = $null

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

$startupDir = [Environment]::GetFolderPath('Startup')
$startupFallback = $null
if (-not [string]::IsNullOrWhiteSpace($startupDir)) {
    New-Item -ItemType Directory -Force -Path $startupDir | Out-Null
    $startupFallback = Join-Path $startupDir 'HeavenSetupRestore.vbs'
    $vbsText = @"
Set shell = CreateObject("WScript.Shell")
shell.Run """$powerShellExe"" -NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File ""$runtimeLauncher""", 0, False
"@
    Set-Content -LiteralPath $startupFallback -Value $vbsText -Encoding ASCII
}

if (-not $taskInstalled -and -not ($startupFallback -and (Test-Path -LiteralPath $startupFallback))) {
    if ($taskError) {
        throw "Could not install startup restore task or fallback. Scheduled task error: $taskError"
    }
    throw 'Could not install startup restore task or fallback.'
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
} | ConvertTo-Json -Compress
