param(
    [string]$RepoRoot = $env:AGENT_CONTROL_REPO,
    [string]$ShortcutName = 'Heaven Agent Control',
    [switch]$AllowNonControllerHost
)

$ErrorActionPreference = 'Stop'

if (-not $AllowNonControllerHost -and $env:COMPUTERNAME -and $env:COMPUTERNAME -ine 'heaven2') {
    throw "Agent Control shortcut must be installed on heaven2. Current host: $env:COMPUTERNAME"
}

if ([string]::IsNullOrWhiteSpace($RepoRoot)) {
    $RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
} else {
    $RepoRoot = (Resolve-Path $RepoRoot).Path
}

$agentDir = Join-Path $RepoRoot 'tools\agent-control'
$launcherBat = Join-Path $agentDir 'Start Agent Control.bat'
if (-not (Test-Path -LiteralPath $launcherBat)) {
    throw "Agent Control launcher not found: $launcherBat"
}

$appDir = Join-Path $env:LOCALAPPDATA 'MHW-Agent-Control'
New-Item -ItemType Directory -Force -Path $appDir | Out-Null

$launcher = Join-Path $appDir 'Launch-Agent-Control.ps1'
$launcherText = @"
`$ErrorActionPreference = 'Stop'
`$env:AGENT_CONTROL_REPO = '$($RepoRoot.Replace("'","''"))'
Set-Location '$($agentDir.Replace("'","''"))'
& '.\Start Agent Control.bat'
"@
Set-Content -LiteralPath $launcher -Value $launcherText -Encoding UTF8

$iconPath = Join-Path $appDir 'agent-control.ico'
Add-Type -AssemblyName System.Drawing
$bmp = New-Object System.Drawing.Bitmap 128,128
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$bg = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(28,32,42))
$ring = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(77,196,255)), 7
$textBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::White)
$g.FillEllipse($bg,4,4,120,120)
$g.DrawArc($ring,22,22,84,84,25,310)
$font = New-Object System.Drawing.Font 'Segoe UI', 30, ([System.Drawing.FontStyle]::Bold), ([System.Drawing.GraphicsUnit]::Pixel)
$format = New-Object System.Drawing.StringFormat
$format.Alignment = [System.Drawing.StringAlignment]::Center
$format.LineAlignment = [System.Drawing.StringAlignment]::Center
$g.DrawString('AC',$font,$textBrush,(New-Object System.Drawing.RectangleF 0,0,128,128),$format)
$hIcon = $bmp.GetHicon()
$icon = [System.Drawing.Icon]::FromHandle($hIcon)
$stream = [System.IO.File]::Create($iconPath)
try {
    $icon.Save($stream)
} finally {
    $stream.Dispose()
    $font.Dispose()
    $format.Dispose()
    $textBrush.Dispose()
    $ring.Dispose()
    $bg.Dispose()
    $g.Dispose()
    $bmp.Dispose()
}

$desktop = [Environment]::GetFolderPath('Desktop')
if ([string]::IsNullOrWhiteSpace($desktop)) {
    throw 'Desktop folder could not be resolved.'
}

$shortcutPath = Join-Path $desktop ($ShortcutName + '.lnk')
$wsh = New-Object -ComObject WScript.Shell
$shortcut = $wsh.CreateShortcut($shortcutPath)
$shortcut.TargetPath = "$env:SystemRoot\System32\WindowsPowerShell\v1.0\powershell.exe"
$shortcut.Arguments = "-NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File `"$launcher`""
$shortcut.WorkingDirectory = $agentDir
$shortcut.Description = 'Open Heaven Agent Control'
$shortcut.IconLocation = "$iconPath,0"
$shortcut.Save()

if (-not (Test-Path -LiteralPath $shortcutPath)) {
    throw "Shortcut was not created: $shortcutPath"
}

$verify = $wsh.CreateShortcut($shortcutPath)
if ($verify.TargetPath -notlike '*powershell.exe') {
    throw "Shortcut target verification failed: $($verify.TargetPath)"
}
if ($verify.WorkingDirectory -ne $agentDir) {
    throw "Shortcut working-directory verification failed: $($verify.WorkingDirectory)"
}
if ($verify.IconLocation -notlike "$iconPath*") {
    throw "Shortcut icon verification failed: $($verify.IconLocation)"
}

[pscustomobject]@{
    created = $true
    path = $shortcutPath
    target = $verify.TargetPath
    working_directory = $verify.WorkingDirectory
    icon_location = $verify.IconLocation
    repo = $RepoRoot
} | ConvertTo-Json -Compress
