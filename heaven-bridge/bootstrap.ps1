$ErrorActionPreference = 'Stop'
$root = Join-Path $env:USERPROFILE 'HeavenBridgeRepo'
$repo = 'https://github.com/fengie/mhw-mods.git'
$branch = 'heaven-bridge'

if (-not (Test-Path (Join-Path $root '.git'))) {
    git clone --branch $branch --single-branch $repo $root
} else {
    git -C $root fetch origin $branch
    git -C $root checkout -B $branch "origin/$branch"
    git -C $root pull --rebase origin $branch
}

$startup = [Environment]::GetFolderPath('Startup')
$launcher = Join-Path $startup 'HeavenBridgeWorker.cmd'
$launcherBody = '@echo off' + [Environment]::NewLine +
    'cd /d "%USERPROFILE%\HeavenBridgeRepo"' + [Environment]::NewLine +
    'pythonw.exe "%USERPROFILE%\HeavenBridgeRepo\heaven-bridge\worker.py"' + [Environment]::NewLine
Set-Content -Path $launcher -Value $launcherBody -Encoding ASCII

Get-CimInstance Win32_Process |
    Where-Object { $_.CommandLine -like '*heaven-bridge\worker.py*' } |
    ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }

Start-Process -WindowStyle Hidden -FilePath 'pythonw.exe' -ArgumentList (Join-Path $root 'heaven-bridge\worker.py') -WorkingDirectory $root
Write-Output 'HEAVEN_BRIDGE_STARTED'
