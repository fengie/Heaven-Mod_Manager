function ConvertTo-AgentControlCanonicalPath {
    param([Parameter(Mandatory = $true)][string]$Path)
    try {
        return [System.IO.Path]::GetFullPath($Path).TrimEnd([char[]]@('\\','/')).ToLowerInvariant()
    } catch {
        return $null
    }
}

function Invoke-AgentControlGit {
    param(
        [Parameter(Mandatory = $true)][string]$RepoRoot,
        [Parameter(Mandatory = $true)][string[]]$Arguments
    )

    $git = Get-Command git.exe -ErrorAction SilentlyContinue
    if (-not $git) { $git = Get-Command git -ErrorAction SilentlyContinue }
    if (-not $git) {
        return [pscustomobject]@{ exit_code = 127; output = 'git executable not found' }
    }

    $output = @(& $git.Source -C $RepoRoot @Arguments 2>&1)
    $exitCode = $LASTEXITCODE
    [pscustomobject]@{
        exit_code = [int]$exitCode
        output = (($output | ForEach-Object { [string]$_ }) -join [Environment]::NewLine).Trim()
    }
}

function New-AgentControlSourceDisposition {
    param(
        [string]$Disposition,
        [bool]$LaunchSafe,
        [bool]$Updated,
        [string]$Branch,
        [string]$BeforeSha,
        [string]$AfterSha,
        [string]$RemoteSha,
        [string]$Reason
    )

    [pscustomobject]@{
        disposition = $Disposition
        launch_safe = $LaunchSafe
        updated = $Updated
        branch = $Branch
        before_sha = $BeforeSha
        after_sha = $AfterSha
        remote_sha = $RemoteSha
        reason = $Reason
        verified_at = (Get-Date).ToUniversalTime().ToString('o')
    }
}

function Get-AgentControlRuntimeSourceDisposition {
    param([Parameter(Mandatory = $true)][string]$RepoRoot)

    try {
        $root = (Resolve-Path -LiteralPath $RepoRoot -ErrorAction Stop).Path
    } catch {
        return New-AgentControlSourceDisposition 'unsafe-preserved' $false $false $null $null $null $null "Repository root could not be resolved: $($_.Exception.Message)"
    }

    $inside = Invoke-AgentControlGit -RepoRoot $root -Arguments @('rev-parse', '--is-inside-work-tree')
    if ($inside.exit_code -ne 0 -or $inside.output -notmatch '(?i)^true$') {
        return New-AgentControlSourceDisposition 'unsafe-preserved' $false $false $null $null $null $null 'Runtime source is not a Git worktree.'
    }

    $branchResult = Invoke-AgentControlGit -RepoRoot $root -Arguments @('symbolic-ref', '--quiet', '--short', 'HEAD')
    $branch = if ($branchResult.exit_code -eq 0) { $branchResult.output.Trim() } else { $null }
    if ($branch -ne 'main') {
        $reason = if ([string]::IsNullOrWhiteSpace($branch)) { 'Runtime source is detached; only canonical main may launch.' } else { "Runtime source is on branch '$branch'; only canonical main may launch." }
        return New-AgentControlSourceDisposition 'unsafe-preserved' $false $false $branch $null $null $null $reason
    }

    $beforeResult = Invoke-AgentControlGit -RepoRoot $root -Arguments @('rev-parse', 'HEAD')
    if ($beforeResult.exit_code -ne 0 -or [string]::IsNullOrWhiteSpace($beforeResult.output)) {
        return New-AgentControlSourceDisposition 'unsafe-preserved' $false $false $branch $null $null $null 'Current runtime source SHA could not be resolved.'
    }
    $before = $beforeResult.output.Trim()

    $status = Invoke-AgentControlGit -RepoRoot $root -Arguments @('status', '--porcelain=v1', '--untracked-files=normal')
    if ($status.exit_code -ne 0) {
        return New-AgentControlSourceDisposition 'unsafe-preserved' $false $false $branch $before $before $null 'Runtime source cleanliness could not be verified.'
    }
    if (-not [string]::IsNullOrWhiteSpace($status.output)) {
        return New-AgentControlSourceDisposition 'unsafe-preserved' $false $false $branch $before $before $null 'Runtime source is dirty or contains untracked files; preserving it without launch.'
    }

    $fetch = Invoke-AgentControlGit -RepoRoot $root -Arguments @('fetch', '--prune', 'origin', 'main')
    if ($fetch.exit_code -ne 0) {
        return New-AgentControlSourceDisposition 'unsafe-preserved' $false $false $branch $before $before $null ("origin/main fetch failed; preserving source without launch. {0}" -f $fetch.output)
    }

    $remoteResult = Invoke-AgentControlGit -RepoRoot $root -Arguments @('rev-parse', 'refs/remotes/origin/main')
    if ($remoteResult.exit_code -ne 0 -or [string]::IsNullOrWhiteSpace($remoteResult.output)) {
        return New-AgentControlSourceDisposition 'unsafe-preserved' $false $false $branch $before $before $null 'Fetched origin/main SHA could not be resolved.'
    }
    $remote = $remoteResult.output.Trim()

    if ($before -eq $remote) {
        return New-AgentControlSourceDisposition 'verified-current' $true $false $branch $before $before $remote 'Canonical main is already current.'
    }

    $ancestor = Invoke-AgentControlGit -RepoRoot $root -Arguments @('merge-base', '--is-ancestor', 'HEAD', 'refs/remotes/origin/main')
    if ($ancestor.exit_code -ne 0) {
        return New-AgentControlSourceDisposition 'unsafe-preserved' $false $false $branch $before $before $remote 'Local main is ahead of or diverged from origin/main; preserving it without launch.'
    }

    $fastForward = Invoke-AgentControlGit -RepoRoot $root -Arguments @('merge', '--ff-only', 'refs/remotes/origin/main')
    if ($fastForward.exit_code -ne 0) {
        return New-AgentControlSourceDisposition 'unsafe-preserved' $false $false $branch $before $before $remote ("Fast-forward failed; preserving source without further mutation. {0}" -f $fastForward.output)
    }

    $afterResult = Invoke-AgentControlGit -RepoRoot $root -Arguments @('rev-parse', 'HEAD')
    $after = if ($afterResult.exit_code -eq 0) { $afterResult.output.Trim() } else { $null }
    if ([string]::IsNullOrWhiteSpace($after) -or $after -ne $remote) {
        return New-AgentControlSourceDisposition 'unsafe-preserved' $false $true $branch $before $after $remote 'Fast-forward completed but exact remote SHA could not be proven; launch remains blocked.'
    }

    return New-AgentControlSourceDisposition 'verified-fast-forwarded' $true $true $branch $before $after $remote 'Canonical main was fast-forwarded exactly to origin/main.'
}

function Test-AgentControlProcessOwnership {
    param(
        [Parameter(Mandatory = $true)][int]$ListenerPid,
        [Parameter(Mandatory = $true)][int]$Port,
        [Parameter(Mandatory = $true)]$Identity,
        [Parameter(Mandatory = $true)]$Process,
        [Parameter(Mandatory = $true)][string]$ExpectedServerPath,
        [int]$CreationToleranceSeconds = 45
    )

    function Deny-AgentControlOwnership([string]$Reason) {
        [pscustomobject]@{ owned = $false; reason = $Reason }
    }

    if ($ListenerPid -le 0) { return Deny-AgentControlOwnership 'Listener PID is missing or invalid.' }
    if ($null -eq $Identity -or $null -eq $Process) { return Deny-AgentControlOwnership 'Persisted identity or observed process is missing.' }
    if ([int]$Identity.pid -ne $ListenerPid) { return Deny-AgentControlOwnership 'Persisted controller PID does not match the listener PID.' }
    if ([int]$Identity.port -ne $Port) { return Deny-AgentControlOwnership 'Persisted controller port does not match the expected port.' }
    if ([int]$Process.ProcessId -ne $ListenerPid) { return Deny-AgentControlOwnership 'Observed process PID does not match the listener PID.' }

    $expectedPath = ConvertTo-AgentControlCanonicalPath -Path $ExpectedServerPath
    $identityPath = ConvertTo-AgentControlCanonicalPath -Path ([string]$Identity.serverPath)
    if ([string]::IsNullOrWhiteSpace($expectedPath) -or [string]::IsNullOrWhiteSpace($identityPath) -or $identityPath -ne $expectedPath) {
        return Deny-AgentControlOwnership 'Persisted serverPath does not match the canonical Agent Control server path.'
    }

    $name = [string]$Process.Name
    $executable = [string]$Process.ExecutablePath
    $command = [string]$Process.CommandLine
    $nodeName = $name -match '(?i)^node(?:\.exe)?$'
    if (-not $nodeName -and -not [string]::IsNullOrWhiteSpace($executable)) {
        $nodeName = [System.IO.Path]::GetFileName($executable) -match '(?i)^node(?:\.exe)?$'
    }
    if (-not $nodeName) { return Deny-AgentControlOwnership 'Observed listener is not a Node process.' }
    if ([string]::IsNullOrWhiteSpace($command) -or $command -notmatch '(?i)(^|[\\/\s"'']+)server\.mjs(?:["''\s]|$)') {
        return Deny-AgentControlOwnership 'Observed Node command line does not identify server.mjs.'
    }

    try {
        $startedAt = [DateTime]::Parse([string]$Identity.startedAt, [Globalization.CultureInfo]::InvariantCulture, [Globalization.DateTimeStyles]::RoundtripKind).ToUniversalTime()
        $createdAt = if ($Process.CreationDate -is [DateTime]) {
            ([DateTime]$Process.CreationDate).ToUniversalTime()
        } else {
            [DateTime]::Parse([string]$Process.CreationDate, [Globalization.CultureInfo]::InvariantCulture, [Globalization.DateTimeStyles]::AssumeLocal).ToUniversalTime()
        }
        $delta = [Math]::Abs(($createdAt - $startedAt).TotalSeconds)
        if ($delta -gt [Math]::Max(1, $CreationToleranceSeconds)) {
            return Deny-AgentControlOwnership ("Observed process creation time differs from persisted startedAt by {0:N1}s." -f $delta)
        }
    } catch {
        return Deny-AgentControlOwnership 'Process creation time could not be matched to persisted startedAt.'
    }

    [pscustomobject]@{ owned = $true; reason = 'PID, port, canonical server path, Node command, and process creation time all match.' }
}
