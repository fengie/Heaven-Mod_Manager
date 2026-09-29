param(
    [string]$Repository = 'fengie/mhw-mods',
    [string]$RunnerName = 'heaven',
    [string]$RunnerDirectory = (Join-Path $env:USERPROFILE 'actions-runner-heaven'),
    [string[]]$Labels = @('heaven-v2','local-bridge','mhw-mods'),
    [switch]$ForceReconfigure
)

$ErrorActionPreference = 'Stop'
$ApiVersion = '2022-11-28'

$isWindows = [System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([System.Runtime.InteropServices.OSPlatform]::Windows)
if (-not $isWindows) { throw 'This installer is intended for the Windows Heaven worker.' }

function Test-IsElevated {
    $identity = [System.Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object System.Security.Principal.WindowsPrincipal($identity)
    return $principal.IsInRole([System.Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Ensure-Elevated {
    if (Test-IsElevated) { return }
    if ([string]::IsNullOrWhiteSpace($PSCommandPath)) {
        throw 'Runner installer needs elevation but PSCommandPath is unavailable.'
    }

    $hostExe = (Get-Process -Id $PID -ErrorAction Stop).Path
    $args = '-NoProfile -ExecutionPolicy Bypass -File "{0}"' -f $PSCommandPath
    Write-Output 'HEAVEN_RUNNER_ELEVATION_REQUESTED'
    $child = Start-Process -FilePath $hostExe -Verb RunAs -ArgumentList $args -Wait -PassThru
    exit $child.ExitCode
}

# Runner registration and bridge CI do not require administrator privileges.
# Avoid UAC prompts so bootstrap remains unattended under the Heaven worker.
$git = (Get-Command git.exe -ErrorAction Stop).Source

function Get-GitHubAccessToken {
    foreach ($name in @('GH_TOKEN','GITHUB_TOKEN')) {
        $value = [Environment]::GetEnvironmentVariable($name)
        if (-not [string]::IsNullOrWhiteSpace($value)) { return $value }
    }

    $request = "protocol=https`nhost=github.com`n`n"
    $response = $request | & $git credential fill 2>$null
    if ($LASTEXITCODE -ne 0) {
        throw 'Git credential manager could not provide a GitHub credential.'
    }

    $password = $null
    foreach ($line in @($response)) {
        if ($line -like 'password=*') {
            $password = $line.Substring('password='.Length)
            break
        }
    }
    $response = $null
    if ([string]::IsNullOrWhiteSpace($password)) {
        throw 'No GitHub token was available from GH_TOKEN, GITHUB_TOKEN, or Git Credential Manager.'
    }
    return $password
}

$script:GitHubAccessToken = Get-GitHubAccessToken

function Invoke-GitHubApi {
    param(
        [Parameter(Mandatory = $true)][ValidateSet('GET','POST','DELETE')][string]$Method,
        [Parameter(Mandatory = $true)][string]$Path
    )

    $headers = @{
        Accept = 'application/vnd.github+json'
        Authorization = "Bearer $script:GitHubAccessToken"
        'X-GitHub-Api-Version' = $ApiVersion
        'User-Agent' = 'Heaven-GitHub-Runner-Bootstrap'
    }

    try {
        return Invoke-RestMethod -Method $Method -Headers $headers -Uri ("https://api.github.com/{0}" -f $Path.TrimStart('/'))
    } catch {
        $status = $null
        try { $status = [int]$_.Exception.Response.StatusCode } catch {}
        if ($status -eq 403) {
            throw 'GitHub rejected the runner administration request (403). The local Git credential must have repository Administration write permission (or classic repo scope) and the account must administer the repository.'
        }
        throw
    } finally {
        $headers.Authorization = $null
    }
}

function Get-RunnerRecord {
    $payload = Invoke-GitHubApi -Method GET -Path "repos/$Repository/actions/runners?per_page=100"
    return @($payload.runners | Where-Object { $_.name -eq $RunnerName }) | Select-Object -First 1
}

function Get-RegistrationToken {
    $payload = Invoke-GitHubApi -Method POST -Path "repos/$Repository/actions/runners/registration-token"
    if ([string]::IsNullOrWhiteSpace([string]$payload.token)) { throw 'GitHub did not return a runner registration token.' }
    return [string]$payload.token
}

function Get-RemovalToken {
    $payload = Invoke-GitHubApi -Method POST -Path "repos/$Repository/actions/runners/remove-token"
    if ([string]::IsNullOrWhiteSpace([string]$payload.token)) { throw 'GitHub did not return a runner removal token.' }
    return [string]$payload.token
}

function Install-RunnerFiles {
    if (Test-Path (Join-Path $RunnerDirectory 'config.cmd')) { return }

    New-Item -ItemType Directory -Force -Path $RunnerDirectory | Out-Null
    $headers = @{
        Accept = 'application/vnd.github+json'
        'User-Agent' = 'Heaven-GitHub-Runner-Bootstrap'
        'X-GitHub-Api-Version' = $ApiVersion
    }
    $release = Invoke-RestMethod -Headers $headers -Uri 'https://api.github.com/repos/actions/runner/releases/latest'
    $version = ([string]$release.tag_name).TrimStart('v')
    if ([string]::IsNullOrWhiteSpace($version)) { throw 'Unable to determine the latest GitHub Actions runner version.' }

    # Multiple bridge/repair agents can race this bootstrap. Use a unique
    # download path so concurrent installers never contend on one temp zip.
    $archive = Join-Path $env:TEMP ("actions-runner-win-x64-{0}-{1}.zip" -f $version, [Guid]::NewGuid().ToString('N'))
    $url = "https://github.com/actions/runner/releases/download/v$version/actions-runner-win-x64-$version.zip"
    Invoke-WebRequest -UseBasicParsing -Uri $url -OutFile $archive
    try { Expand-Archive -LiteralPath $archive -DestinationPath $RunnerDirectory -Force }
    finally { Remove-Item -LiteralPath $archive -Force -ErrorAction SilentlyContinue }

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
        & (Join-Path $RunnerDirectory 'config.cmd') remove --token $token
        if ($LASTEXITCODE -ne 0) { throw "config.cmd remove failed with exit code $LASTEXITCODE." }
    } finally {
        Pop-Location
        $token = $null
    }
}

function Configure-Runner {
    $token = Get-RegistrationToken
    $labelText = ($Labels | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -Unique) -join ','
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
        if ($LASTEXITCODE -ne 0) { throw "config.cmd failed with exit code $LASTEXITCODE." }
    } finally {
        Pop-Location
        $token = $null
    }
}

function Get-RunnerListener {
    Get-CimInstance Win32_Process -ErrorAction SilentlyContinue |
        Where-Object {
            $_.Name -eq 'Runner.Listener.exe' -and
            $_.ExecutablePath -and
            $_.ExecutablePath.StartsWith($RunnerDirectory, [System.StringComparison]::OrdinalIgnoreCase)
        } |
        Select-Object -First 1
}

function Ensure-RunnerLaunch {
    $taskName = "GitHub Actions Runner - $RunnerName"
    $runCmd = Join-Path $RunnerDirectory 'run.cmd'
    if (-not (Test-Path $runCmd)) { throw "Runner entrypoint missing: $runCmd" }

    $cmdExe = Join-Path ([Environment]::SystemDirectory) 'cmd.exe'
    if (-not (Test-Path -LiteralPath $cmdExe)) { throw "Windows command processor missing: $cmdExe" }

    try {
        Stop-RunnerTask -TaskName $taskName
        $identityName = [System.Security.Principal.WindowsIdentity]::GetCurrent().Name
        $action = New-ScheduledTaskAction -Execute $cmdExe -Argument ('/d /c ""{0}""' -f $runCmd) -WorkingDirectory $RunnerDirectory
        $trigger = New-ScheduledTaskTrigger -AtLogOn
        $settings = New-ScheduledTaskSettingsSet `
            -AllowStartIfOnBatteries `
            -DontStopIfGoingOnBatteries `
            -StartWhenAvailable `
            -MultipleInstances IgnoreNew `
            -RestartCount 999 `
            -RestartInterval (New-TimeSpan -Minutes 1) `
            -ExecutionTimeLimit ([TimeSpan]::Zero)
        $principal = New-ScheduledTaskPrincipal -UserId $identityName -LogonType Interactive -RunLevel Limited

        Register-ScheduledTask -TaskName $taskName -Action $action -Trigger $trigger -Settings $settings -Principal $principal -Force | Out-Null
        $registered = Get-ScheduledTask -TaskName $taskName -ErrorAction Stop
        if ([string]$registered.Principal.RunLevel -ne 'Limited') {
            throw "Runner task registered with unexpected RunLevel '$($registered.Principal.RunLevel)'."
        }
        Start-ScheduledTask -TaskName $taskName
        return [ordered]@{
            mode = 'scheduled-task'
            scheduled_task = $taskName
            scheduled_task_run_level = [string]$registered.Principal.RunLevel
            startup_fallback = $null
        }
    } catch {
        # Some locked-down user sessions deny task registration even at Limited.
        # GitHub's runner does not need admin; persist it through the per-user
        # Startup folder instead of turning that policy into a CI blocker.
        Write-Warning "Scheduled task unavailable; using per-user Startup fallback. $($_.Exception.Message)"

        $startup = [Environment]::GetFolderPath('Startup')
        $safeRunnerName = ($RunnerName -replace '[^A-Za-z0-9._-]', '-')
        $startupFile = if ($RunnerName -eq 'heaven') { 'HeavenGitHubActionsRunner.vbs' } else { "HeavenGitHubActionsRunner-$safeRunnerName.vbs" }
        $startupVbs = Join-Path $startup $startupFile
        $escapedCmd = $cmdExe.Replace('"', '""')
        $escapedRun = $runCmd.Replace('"', '""')
        $escapedCwd = $RunnerDirectory.Replace('"', '""')
        $vbs = @"
Set sh = CreateObject("WScript.Shell")
sh.CurrentDirectory = "$escapedCwd"
sh.Run """$escapedCmd"" /d /c """"$escapedRun""""", 0, False
"@
        Set-Content -LiteralPath $startupVbs -Value $vbs -Encoding ASCII

        if (-not (Get-RunnerListener)) {
            Start-Process -FilePath $cmdExe -ArgumentList ('/d /c ""{0}""' -f $runCmd) -WorkingDirectory $RunnerDirectory -WindowStyle Hidden | Out-Null
        }

        return [ordered]@{
            mode = 'startup-fallback'
            scheduled_task = $null
            scheduled_task_run_level = $null
            startup_fallback = $startupVbs
        }
    }
}

try {
    Install-RunnerFiles

    $taskName = "GitHub Actions Runner - $RunnerName"
    $configured = Test-Path (Join-Path $RunnerDirectory '.runner')

    # Existing runner registrations can silently drift from the labels expected by
    # workflow YAML. Detect that drift and reconfigure automatically so jobs do
    # not remain queued forever with runner_id=0.
    $desiredLabels = @($Labels | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -Unique)
    $recordBefore = $null
    $missingLabels = @()
    if ($configured) {
        try { $recordBefore = Get-RunnerRecord } catch {}
        if ($recordBefore) {
            $actualLabels = @($recordBefore.labels | ForEach-Object { [string]$_.name })
            $missingLabels = @($desiredLabels | Where-Object { $_ -notin $actualLabels })
        } else {
            $missingLabels = @($desiredLabels)
        }
    }

    $needsReconfigure = [bool]($ForceReconfigure -or ($configured -and $missingLabels.Count -gt 0))
    if ($needsReconfigure -and $configured) {
        Stop-RunnerTask -TaskName $taskName
        Remove-ExistingConfiguration
        $configured = $false
    }
    if (-not $configured) { Configure-Runner }

    $launch = Ensure-RunnerLaunch

    $deadline = (Get-Date).AddMinutes(2)
    $record = $null
    while ((Get-Date) -lt $deadline) {
        Start-Sleep -Seconds 2
        $record = Get-RunnerRecord
        if ($record -and [string]$record.status -eq 'online') { break }
    }

    if (-not $record) { throw "Runner '$RunnerName' is not registered with $Repository." }
    if ([string]$record.status -ne 'online') { throw "Runner '$RunnerName' is registered but not online. Status: $($record.status)." }

    [ordered]@{
        repository = $Repository
        runner_name = $RunnerName
        runner_id = $record.id
        status = $record.status
        busy = [bool]$record.busy
        labels = @($record.labels | ForEach-Object { $_.name })
        runner_directory = $RunnerDirectory
        launch_mode = $launch.mode
        scheduled_task = $launch.scheduled_task
        scheduled_task_run_level = $launch.scheduled_task_run_level
        startup_fallback = $launch.startup_fallback
        elevated = [bool](Test-IsElevated)
        auth_source = 'local-git-credential'
    } | ConvertTo-Json -Depth 5
} finally {
    $script:GitHubAccessToken = $null
}
