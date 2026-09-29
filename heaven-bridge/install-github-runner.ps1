$ErrorActionPreference = 'Stop'

$Repository = 'fengie/mhw-mods'
$RunnerName = 'heaven'
$RunnerDirectory = Join-Path $env:USERPROFILE 'actions-runner-heaven'
$Labels = @('heaven','local-bridge','mhw-mods')
$ForceReconfigure = $false

$git = (Get-Command git.exe -ErrorAction Stop).Source

function Get-GitHubAccessToken {
    $request = "protocol=https`nhost=github.com`n`n"
    $raw = $request | & $git credential fill
    if ($LASTEXITCODE -ne 0) {
        throw 'Git Credential Manager could not supply the existing GitHub credential.'
    }
    $fields = @{}
    foreach ($line in @($raw)) {
        if ([string]$line -match '^([^=]+)=(.*)$') {
            $fields[$Matches[1]] = $Matches[2]
        }
    }
    $token = [string]$fields['password']
    if ([string]::IsNullOrWhiteSpace($token)) {
        throw 'No GitHub access token is available from the existing Git credential.'
    }
    return $token
}

$script:GitHubToken = Get-GitHubAccessToken

function Invoke-GitHubApi {
    param(
        [Parameter(Mandatory = $true)][ValidateSet('GET','POST')][string]$Method,
        [Parameter(Mandatory = $true)][string]$Path
    )
    $headers = @{
        Accept = 'application/vnd.github+json'
        Authorization = "Bearer $script:GitHubToken"
        'User-Agent' = 'Heaven-GitHub-Runner-Bootstrap'
        'X-GitHub-Api-Version' = '2022-11-28'
    }
    return Invoke-RestMethod -Method $Method -Headers $headers -Uri ("https://api.github.com/" + $Path.TrimStart('/'))
}

function Get-RunnerRecord {
    $payload = Invoke-GitHubApi -Method GET -Path "repos/$Repository/actions/runners"
    return @($payload.runners | Where-Object { $_.name -eq $RunnerName }) | Select-Object -First 1
}

function Get-RegistrationToken {
    $payload = Invoke-GitHubApi -Method POST -Path "repos/$Repository/actions/runners/registration-token"
    if ([string]::IsNullOrWhiteSpace([string]$payload.token)) {
        throw 'GitHub did not return a runner registration token.'
    }
    return [string]$payload.token
}

function Get-RemovalToken {
    $payload = Invoke-GitHubApi -Method POST -Path "repos/$Repository/actions/runners/remove-token"
    if ([string]::IsNullOrWhiteSpace([string]$payload.token)) {
        throw 'GitHub did not return a runner removal token.'
    }
    return [string]$payload.token
}

function Install-RunnerFiles {
    if (Test-Path (Join-Path $RunnerDirectory 'config.cmd')) { return }

    New-Item -ItemType Directory -Force -Path $RunnerDirectory | Out-Null
    $release = Invoke-GitHubApi -Method GET -Path 'repos/actions/runner/releases/latest'
    $version = ([string]$release.tag_name).TrimStart('v')
    if ([string]::IsNullOrWhiteSpace($version)) {
        throw 'Unable to determine the latest GitHub Actions runner version.'
    }

    $archive = Join-Path $env:TEMP "actions-runner-win-x64-$version.zip"
    $url = "https://github.com/actions/runner/releases/download/v$version/actions-runner-win-x64-$version.zip"
    Invoke-WebRequest -UseBasicParsing -Uri $url -OutFile $archive
    try {
        Expand-Archive -LiteralPath $archive -DestinationPath $RunnerDirectory -Force
    } finally {
        Remove-Item -LiteralPath $archive -Force -ErrorAction SilentlyContinue
    }

    if (-not (Test-Path (Join-Path $RunnerDirectory 'config.cmd'))) {
        throw 'Runner archive extracted but config.cmd is missing.'
    }
}

function Stop-RunnerTask {
    param([string]$TaskName)
    try { Stop-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue } catch {}
    Start-Sleep -Milliseconds 500
}

function Remove-ExistingConfiguration {
    if (-not (Test-Path (Join-Path $RunnerDirectory '.runner'))) { return }
    $token = Get-RemovalToken
    Push-Location $RunnerDirectory
    try {
        & (Join-Path $RunnerDirectory 'config.cmd') remove --unattended --token $token
        if ($LASTEXITCODE -ne 0) {
            throw "config.cmd remove failed with exit code $LASTEXITCODE."
        }
    } finally {
        Pop-Location
        $token = $null
    }
}

function Configure-Runner {
    $token = Get-RegistrationToken
    $labelText = ($Labels | Select-Object -Unique) -join ','
    $configArgs = @(
        '--unattended',
        '--url', "https://github.com/$Repository",
        '--token', $token,
        '--name', $RunnerName,
        '--labels', $labelText,
        '--work', '_work',
        '--replace'
    )
    Push-Location $RunnerDirectory
    try {
        & (Join-Path $RunnerDirectory 'config.cmd') @configArgs
        if ($LASTEXITCODE -ne 0) {
            throw "config.cmd failed with exit code $LASTEXITCODE."
        }
    } finally {
        Pop-Location
        $token = $null
    }
}

function Ensure-RunnerScheduledTask {
    $taskName = "GitHub Actions Runner - $RunnerName"
    $runCmd = Join-Path $RunnerDirectory 'run.cmd'
    if (-not (Test-Path $runCmd)) {
        throw "Runner entrypoint missing: $runCmd"
    }

    Stop-RunnerTask -TaskName $taskName
    $action = New-ScheduledTaskAction -Execute $env:ComSpec -Argument ('/d /c ""{0}""' -f $runCmd) -WorkingDirectory $RunnerDirectory
    $trigger = New-ScheduledTaskTrigger -AtLogOn
    $settings = New-ScheduledTaskSettingsSet `
        -AllowStartIfOnBatteries `
        -DontStopIfGoingOnBatteries `
        -StartWhenAvailable `
        -MultipleInstances IgnoreNew `
        -RestartCount 999 `
        -RestartInterval (New-TimeSpan -Minutes 1)
    Register-ScheduledTask -TaskName $taskName -Action $action -Trigger $trigger -Settings $settings -Force | Out-Null
    Start-ScheduledTask -TaskName $taskName
    return $taskName
}

Install-RunnerFiles

$taskName = "GitHub Actions Runner - $RunnerName"
$configured = Test-Path (Join-Path $RunnerDirectory '.runner')
if ($ForceReconfigure -and $configured) {
    Stop-RunnerTask -TaskName $taskName
    Remove-ExistingConfiguration
    $configured = $false
}
if (-not $configured) {
    Configure-Runner
}

$taskName = Ensure-RunnerScheduledTask

$deadline = (Get-Date).AddMinutes(2)
$record = $null
while ((Get-Date) -lt $deadline) {
    Start-Sleep -Seconds 2
    $record = Get-RunnerRecord
    if ($record -and [string]$record.status -eq 'online') { break }
}

if (-not $record) {
    throw "Runner '$RunnerName' is not registered with $Repository."
}
if ([string]$record.status -ne 'online') {
    throw "Runner '$RunnerName' is registered but not online. Status: $($record.status)."
}

[ordered]@{
    repository = $Repository
    runner_name = $RunnerName
    runner_id = $record.id
    status = $record.status
    busy = [bool]$record.busy
    labels = @($record.labels | ForEach-Object { $_.name })
    runner_directory = $RunnerDirectory
    scheduled_task = $taskName
} | ConvertTo-Json -Depth 5

$script:GitHubToken = $null
