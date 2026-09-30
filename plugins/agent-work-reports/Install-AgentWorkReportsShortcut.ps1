param(
    [string]$ShortcutName = 'Agent Work Reports'
)

$ErrorActionPreference = 'Stop'
$Root = Split-Path -Parent $MyInvocation.MyCommand.Path
$Launcher = Join-Path $Root 'Start-AgentWorkReports.ps1'
$Desktop = [Environment]::GetFolderPath('Desktop')
$ShortcutPath = Join-Path $Desktop ($ShortcutName + '.lnk')
$PowerShell = (Get-Command powershell.exe).Source

$Shell = New-Object -ComObject WScript.Shell
$Shortcut = $Shell.CreateShortcut($ShortcutPath)
$Shortcut.TargetPath = $PowerShell
$Shortcut.Arguments = '-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File "' + $Launcher + '"'
$Shortcut.WorkingDirectory = $Root
$Shortcut.Description = 'Open the local Agent Work Reports progress dashboard'
$Shortcut.Save()

Write-Host "Installed shortcut: $ShortcutPath"
