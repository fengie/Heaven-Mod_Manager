param(
    [string]$Root = ''
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if ([string]::IsNullOrWhiteSpace($Root)) {
    $Root = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
}
else {
    $Root = (Resolve-Path -LiteralPath $Root).Path
}

$workflowRoot = Join-Path $Root '.github\workflows'
if (!(Test-Path -LiteralPath $workflowRoot)) {
    throw "Workflow directory is missing: $workflowRoot"
}

$writeAllowlist = @(
    'branch-lifecycle-enforcer.yml',
    'updater-installed-client-e2e.yml',
    'windows-release-gate.yml'
)

$errors = New-Object System.Collections.Generic.List[string]
$workflows = @(
    Get-ChildItem -LiteralPath $workflowRoot -File |
        Where-Object { $_.Extension -in @('.yml', '.yaml') } |
        Sort-Object Name
)

foreach ($workflow in $workflows) {
    $content = Get-Content -LiteralPath $workflow.FullName -Raw
    $lines = @(Get-Content -LiteralPath $workflow.FullName)

    if ($content -notmatch '(?m)^permissions:\s*$') {
        $errors.Add("$($workflow.Name): missing explicit top-level permissions block.")
    }
    if ($content -match '(?mi)^\s*permissions:\s*write-all\s*$') {
        $errors.Add("$($workflow.Name): permissions: write-all is forbidden.")
    }
    if ($content -match '(?mi)^\s*pull_request_target:\s*$') {
        $errors.Add("$($workflow.Name): pull_request_target is forbidden because it can combine trusted credentials/context with attacker-controlled PR input.")
    }
    if ($content -match '(?i)NuGetAudit\s*=\s*false') {
        $errors.Add("$($workflow.Name): NuGet vulnerability auditing must not be disabled in CI.")
    }

    $hasPullRequest = $content -match '(?m)^\s{2}pull_request:\s*$'
    $usesSelfHosted = $content -match '(?mi)^\s+runs-on:\s*\[\s*self-hosted\b'
    $hasSameRepoGuard = $content -match "github\.event_name\s*!=\s*'pull_request'\s*\|\|\s*github\.event\.pull_request\.head\.repo\.full_name\s*==\s*github\.repository"

    if ($hasPullRequest -and $usesSelfHosted -and -not $hasSameRepoGuard) {
        $errors.Add("$($workflow.Name): self-hosted pull_request execution requires an explicit same-repository head guard; fork PR code must never run on a persistent runner.")
    }

    if ($hasPullRequest) {
        if ($content -match '(?i)\bsecrets\.[A-Za-z0-9_]+') {
            $errors.Add("$($workflow.Name): pull_request validation must not reference repository secrets; move privileged work to a trusted post-merge/push workflow.")
        }
        if ($content -match '(?mi)^\s{2}[A-Za-z][A-Za-z0-9-]*:\s*write\s*$') {
            $errors.Add("$($workflow.Name): pull_request validation must keep GITHUB_TOKEN read-only.")
        }
    }

    if ($usesSelfHosted) {
        $checkoutCount = [regex]::Matches($content, '(?mi)^\s+uses:\s*actions/checkout@[0-9a-f]{40}\b').Count
        $safeCheckoutCount = [regex]::Matches($content, '(?mi)^\s+persist-credentials:\s*false\s*$').Count
        if ($safeCheckoutCount -lt $checkoutCount) {
            $errors.Add("$($workflow.Name): every checkout on a persistent self-hosted runner must set persist-credentials: false.")
        }
    }

    if ($content -match '(?mi)^\s*contents:\s*write\s*$' -and $writeAllowlist -notcontains $workflow.Name) {
        $errors.Add("$($workflow.Name): contents: write is not approved for this workflow. Keep GITHUB_TOKEN read-only unless a documented mutation requires it.")
    }

    if ($usesSelfHosted -and $writeAllowlist -notcontains $workflow.Name -and
        $content -match '(?mi)^\s{2}[A-Za-z][A-Za-z0-9-]*:\s*write\s*$') {
        $errors.Add("$($workflow.Name): persistent self-hosted code execution must not carry write-capable GITHUB_TOKEN scopes. Split privileged mutation into an explicitly reviewed trusted workflow.")
    }

    for ($i = 0; $i -lt $lines.Count; $i++) {
        $trim = $lines[$i].Trim()
        if ($trim -notmatch '^uses:\s*([^\s#]+)') { continue }
        $target = $Matches[1]
        if ($target.StartsWith('./')) { continue }
        if ($target -notmatch '^([^@]+)@([0-9a-fA-F]{40})$') {
            $errors.Add("$($workflow.Name):$($i + 1): external action '$target' must be pinned to a full 40-character commit SHA.")
        }
    }
}

# Reject high-risk primitives in tracked source. These are deliberately narrow
# patterns; broad heuristic findings belong in review, not a release-blocking gate.
$git = (Get-Command git -ErrorAction Stop).Source
$trackedSource = @(& $git -C $Root ls-files -- '*.cs' '*.py')
if ($LASTEXITCODE -ne 0) {
    $errors.Add('Unable to enumerate tracked C#/Python files for primitive security checks.')
}
else {
    foreach ($relative in $trackedSource) {
        if ([string]::IsNullOrWhiteSpace($relative)) { continue }
        $full = Join-Path $Root $relative
        if (!(Test-Path -LiteralPath $full -PathType Leaf)) { continue }
        $text = Get-Content -LiteralPath $full -Raw -ErrorAction SilentlyContinue
        if ($null -eq $text) { continue }

        if ($relative.EndsWith('.cs', [StringComparison]::OrdinalIgnoreCase)) {
            if ($text -match 'DangerousAcceptAnyServerCertificateValidator|ServerCertificateCustomValidationCallback\s*=\s*[^;\r\n]*=>\s*true') {
                $errors.Add("$($relative): TLS certificate validation bypass detected.")
            }
            if ($text -match '\b(?:BinaryFormatter|NetDataContractSerializer|LosFormatter)\b') {
                $errors.Add("$($relative): unsafe legacy formatter detected.")
            }
        }
        elseif ($relative.EndsWith('.py', [StringComparison]::OrdinalIgnoreCase)) {
            if ($text -match '\bshell\s*=\s*True\b') {
                $errors.Add("$($relative): Python shell=True command execution detected.")
            }
            if ($text -match '\bpickle\.(?:loads?|Unpickler)\b') {
                $errors.Add("$($relative): unsafe Python deserialization detected.")
            }
            if ($text -match '(?m)^\s*(?:eval|exec)\s*\(') {
                $errors.Add("$($relative): dynamic Python code execution detected.")
            }
        }
    }
}

$updaterRoot = Join-Path $Root 'src\MhwModManager.Updater'
if (Test-Path -LiteralPath $updaterRoot) {
    foreach ($file in @(Get-ChildItem -LiteralPath $updaterRoot -Recurse -File -Filter '*.cs')) {
        $text = Get-Content -LiteralPath $file.FullName -Raw
        $relative = [IO.Path]::GetRelativePath($Root, $file.FullName).Replace('\', '/')
        if ($text -match '(?i)http://') {
            $errors.Add("$($relative): updater network code must not contain plaintext HTTP endpoints.")
        }
        if ($text -match '\bZipFile\.ExtractToDirectory\s*\(') {
            $errors.Add("$($relative): updater must use bounded path-safe extraction instead of ZipFile.ExtractToDirectory.")
        }
    }
}

$propsPath = Join-Path $Root 'Directory.Build.props'
if (!(Test-Path -LiteralPath $propsPath)) {
    $errors.Add('Directory.Build.props is missing.')
}
else {
    $props = Get-Content -LiteralPath $propsPath -Raw
    foreach ($required in @(
        '<NuGetAudit>true</NuGetAudit>',
        '<NuGetAuditMode>all</NuGetAuditMode>',
        '<NuGetAuditLevel>low</NuGetAuditLevel>'
    )) {
        if (-not $props.Contains($required)) {
            $errors.Add("Directory.Build.props: required vulnerability-audit invariant is missing: $required")
        }
    }
    if ($props -match '(?i)<NuGetAudit>\s*false\s*</NuGetAudit>') {
        $errors.Add('Directory.Build.props: NuGet vulnerability auditing must not be disabled.')
    }
}

$releasePath = Join-Path $workflowRoot 'windows-release-gate.yml'
if (!(Test-Path -LiteralPath $releasePath)) {
    $errors.Add('windows-release-gate.yml is missing.')
}
else {
    $release = Get-Content -LiteralPath $releasePath -Raw
    if ($release.Contains('repos/cli/cli/releases/latest') -or $release.Contains('Get-Command gh')) {
        $errors.Add('windows-release-gate.yml: privileged release tooling must not trust a moving latest release or arbitrary preinstalled gh.exe.')
    }
    foreach ($required in @(
        '$version = ''2.101.0''',
        'bc6c814367b193cd8e713611d61e36013c0ef843b8f516458fe3eda039192794',
        'Get-FileHash',
        '$actualSha256 -ne $expectedSha256',
        ('https://github.com/cli/cli/releases/download/v$' + '{version}/$' + '{assetName}')
    )) {
        if (-not $release.Contains($required)) {
            $errors.Add("windows-release-gate.yml: verified GitHub CLI bootstrap invariant missing: $required")
        }
    }
}

$secretLeakPolicy = Join-Path $Root 'scripts\testing\Test-TrackedSecretLeaks.ps1'
if (!(Test-Path -LiteralPath $secretLeakPolicy)) {
    $errors.Add('Tracked-secret leak policy is missing.')
}
else {
    try {
        & $secretLeakPolicy -Root $Root
    }
    catch {
        $errors.Add("Tracked-secret leak policy failed: $($_.Exception.Message)")
    }
}

if ($errors.Count -gt 0) {
    Write-Host "CI security policy failed with $($errors.Count) violation(s):" -ForegroundColor Red
    foreach ($item in $errors) {
        Write-Host " - $item" -ForegroundColor Red
    }
    throw 'CI security policy rejected the workflow/source set.'
}

Write-Host "PASS: CI security policy ($($workflows.Count) workflows checked)." -ForegroundColor Green
