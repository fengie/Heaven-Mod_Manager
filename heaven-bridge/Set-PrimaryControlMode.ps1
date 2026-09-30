param(
    [switch]$KeepRdcRunning
)

$ErrorActionPreference = 'Stop'

$StateDir = Join-Path $env:USERPROFILE 'HeavenBridge'
$AuthDir = Join-Path $StateDir 'auth'
$RepoAclMarker = Join-Path $AuthDir 'allow-repo-acl-only'
$RdcTaskName = 'Remote Desktop Commander Device'
$RdcLauncher = Join-Path $StateDir 'Start-RemoteDesktopCommander.ps1'

New-Item -ItemType Directory -Force -Path $AuthDir | Out-Null
if (-not (Test-Path -LiteralPath $RepoAclMarker)) {
    Set-Content -LiteralPath $RepoAclMarker -Value 'private-repo-acl' -Encoding ASCII
}

$rdc = [ordered]@{
    configured = $false
    on_demand_only = $false
    running = $false
}

$existing = Get-ScheduledTask -TaskName $RdcTaskName -ErrorAction SilentlyContinue
if ($existing -or (Test-Path -LiteralPath $RdcLauncher)) {
    if (-not (Test-Path -LiteralPath $RdcLauncher)) {
        throw "RDC fallback task exists but launcher is missing: $RdcLauncher"
    }

    $powershell = (Get-Command powershell.exe -ErrorAction Stop).Source
    $userIdentity = "$env:USERDOMAIN\$env:USERNAME"
    $actionParams = @{
        Execute = $powershell
        Argument = ('-NoLogo -NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File "{0}"' -f $RdcLauncher)
        WorkingDirectory = $StateDir
    }
    $action = New-ScheduledTaskAction @actionParams
    $principal = New-ScheduledTaskPrincipal -UserId $userIdentity -LogonType Interactive -RunLevel Limited
    $settingsParams = @{
        AllowStartIfOnBatteries = $true
        DontStopIfGoingOnBatteries = $true
        StartWhenAvailable = $true
        MultipleInstances = 'IgnoreNew'
        RestartCount = 99
        RestartInterval = (New-TimeSpan -Minutes 1)
        ExecutionTimeLimit = [TimeSpan]::Zero
    }
    $settings = New-ScheduledTaskSettingsSet @settingsParams

    if ($existing) {
        Stop-ScheduledTask -TaskName $RdcTaskName -ErrorAction SilentlyContinue
    }

    # Deliberately omit triggers: RDC stays paired/configured but only starts
    # when an operator explicitly requests the fallback.
    $registerParams = @{
        TaskName = $RdcTaskName
        Action = $action
        Principal = $principal
        Settings = $settings
        Force = $true
    }
    Register-ScheduledTask @registerParams | Out-Null

    if ($KeepRdcRunning) {
        Start-ScheduledTask -TaskName $RdcTaskName
    }

    $task = Get-ScheduledTask -TaskName $RdcTaskName -ErrorAction Stop
    $rdc.configured = $true
    $rdc.on_demand_only = @($task.Triggers).Count -eq 0
    $rdc.running = [string]$task.State -eq 'Running'
}

[ordered]@{
    host = $env:COMPUTERNAME.ToLowerInvariant()
    bridge_primary = $true
    repo_acl_marker = Test-Path -LiteralPath $RepoAclMarker
    rdc = $rdc
} | ConvertTo-Json -Depth 5
