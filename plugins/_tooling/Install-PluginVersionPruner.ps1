param(
    [string]$RepoRoot = $env:AGENT_CONTROL_REPO,
    [string]$TaskName = 'MHW Plugin Version Pruner'
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($RepoRoot)) {
    $RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
} else {
    $RepoRoot = (Resolve-Path $RepoRoot).Path
}

$source = Join-Path $RepoRoot 'plugins\_tooling\prune_outdated_plugins.py'
if (-not (Test-Path -LiteralPath $source)) { throw "Plugin version pruner source not found: $source" }

$runtimeDir = Join-Path $env:LOCALAPPDATA 'MHW-Plugin-Maintenance'
New-Item -ItemType Directory -Force -Path $runtimeDir | Out-Null
$runtime = Join-Path $runtimeDir 'prune_outdated_plugins.py'
$logPath = Join-Path $runtimeDir 'last-prune.json'
Copy-Item -LiteralPath $source -Destination $runtime -Force

$python = $null
foreach ($candidate in @('pythonw.exe', 'python.exe')) {
    try { $python = (Get-Command $candidate -ErrorAction Stop).Source; if ($python) { break } } catch {}
}
if (-not $python) { throw 'Python was not found; cannot install plugin version pruning.' }

$arguments = ('"{0}" --apply --repo-root "{1}" --log "{2}"' -f $runtime, $RepoRoot, $logPath)
$userId = [System.Security.Principal.WindowsIdentity]::GetCurrent().Name
$action = New-ScheduledTaskAction -Execute $python -Argument $arguments
$logonTrigger = New-ScheduledTaskTrigger -AtLogOn -User $userId
$dailyTrigger = New-ScheduledTaskTrigger -Daily -At '03:15'
$settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -StartWhenAvailable -MultipleInstances IgnoreNew -RestartCount 3 -RestartInterval (New-TimeSpan -Minutes 1)
$principal = New-ScheduledTaskPrincipal -UserId $userId -LogonType Interactive -RunLevel Limited
Register-ScheduledTask -TaskName $TaskName -Action $action -Trigger @($logonTrigger, $dailyTrigger) -Settings $settings -Principal $principal -Force | Out-Null

$startup = [Environment]::GetFolderPath('Startup')
$fallback = $null
if (-not [string]::IsNullOrWhiteSpace($startup)) {
    New-Item -ItemType Directory -Force -Path $startup | Out-Null
    $fallback = Join-Path $startup 'MHWPluginVersionPruner.vbs'
    $escapedPython = $python.Replace('"', '""')
    $escapedRuntime = $runtime.Replace('"', '""')
    $escapedRepo = $RepoRoot.Replace('"', '""')
    $escapedLog = $logPath.Replace('"', '""')
    $vbs = 'Set shell = CreateObject("WScript.Shell")' + [Environment]::NewLine + 'shell.Run """' + $escapedPython + '"" ""' + $escapedRuntime + '"" --apply --repo-root ""' + $escapedRepo + '"" --log ""' + $escapedLog + '""", 0, False'
    Set-Content -LiteralPath $fallback -Value $vbs -Encoding ASCII
}

Start-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue

[pscustomobject]@{
    installed = $true
    task_name = $TaskName
    task_state = [string](Get-ScheduledTask -TaskName $TaskName -ErrorAction Stop).State
    runtime = $runtime
    log = $logPath
    startup_fallback = $fallback
    repo = $RepoRoot
} | ConvertTo-Json -Compress
