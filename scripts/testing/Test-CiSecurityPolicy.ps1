param(
    [string]$Root = ''
)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest

if([string]::IsNullOrWhiteSpace($Root)){
    $Root=(Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
}else{
    $Root=(Resolve-Path -LiteralPath $Root).Path
}

$workflowRoot=Join-Path $Root '.github\workflows'
if(!(Test-Path -LiteralPath $workflowRoot)){throw "Workflow directory is missing: $workflowRoot"}

$writeAllowlist=@(
    'branch-lifecycle-enforcer.yml',
    'updater-installed-client-e2e.yml',
    'windows-release-gate.yml'
)
$errors=New-Object System.Collections.Generic.List[string]
$workflows=@(Get-ChildItem -LiteralPath $workflowRoot -File | Where-Object {$_.Extension -in @('.yml','.yaml')} | Sort-Object Name)

foreach($workflow in $workflows){
    $content=Get-Content -LiteralPath $workflow.FullName -Raw
    $lines=@(Get-Content -LiteralPath $workflow.FullName)

    if($content -notmatch '(?m)^permissions:\s*$'){
        $errors.Add("$($workflow.Name): missing explicit top-level permissions block.")
    }
    if($content -match '(?mi)^\s*permissions:\s*write-all\s*$'){
        $errors.Add("$($workflow.Name): permissions: write-all is forbidden.")
    }
    if($content -match '(?mi)^\s*pull_request_target:\s*$'){
        $errors.Add("$($workflow.Name): pull_request_target is forbidden because it combines trusted credentials/context with attacker-controlled PR metadata or code.")
    }
    if($content -match '(?i)NuGetAudit\s*=\s*false'){
        $errors.Add("$($workflow.Name): NuGet vulnerability auditing must not be disabled in CI.")
    }

    $hasPullRequest=$content -match '(?m)^\s{2}pull_request:\s*$'
    $usesSelfHosted=$content -match '(?i)runs-on:\s*\[\s*self-hosted\b'
    $hasSameRepoGuard=$content -match "github\.event_name\s*!=\s*'pull_request'\s*\|\|\s*github\.event\.pull_request\.head\.repo\.full_name\s*==\s*github\.repository"
    if($hasPullRequest -and $usesSelfHosted -and -not $hasSameRepoGuard){
        $errors.Add("$($workflow.Name): self-hosted pull_request execution requires an explicit same-repository head guard; fork PR code must never run on a persistent runner.")
    }

    if($hasPullRequest){
        if($content -match '(?i)\bsecrets\.[A-Za-z0-9_]+'){
            $errors.Add("$($workflow.Name): pull_request validation must not reference repository secrets; move privileged work to a trusted post-merge/push workflow.")
        }
        if($content -match '(?mi)^\s{2}[A-Za-z][A-Za-z0-9-]*:\s*write\s*$'){
            $errors.Add("$($workflow.Name): pull_request validation must keep GITHUB_TOKEN read-only.")
        }
    }

    if($hasPullRequest -and $usesSelfHosted){
        $checkoutCount=[regex]::Matches($content,'(?mi)^\s+uses:\s*actions/checkout@[0-9a-f]{40}\b').Count
        $safeCheckoutCount=[regex]::Matches($content,'(?mi)^\s+persist-credentials:\s*false\s*$').Count
        if($safeCheckoutCount -lt $checkoutCount){
            $errors.Add("$($workflow.Name): every self-hosted pull_request checkout must set persist-credentials: false.")
        }
    }

    if($content -match '(?mi)^\s*contents:\s*write\s*$' -and $writeAllowlist -notcontains $workflow.Name){
        $errors.Add("$($workflow.Name): contents: write is not approved for this workflow. Keep GITHUB_TOKEN read-only unless a documented mutation requires it.")
    }

    for($i=0;$i -lt $lines.Count;$i++){
        $trim=$lines[$i].Trim()
        if($trim -notmatch '^uses:\s*([^\s#]+)'){continue}
        $target=$Matches[1]
        if($target.StartsWith('./')){continue}
        if($target -notmatch '^([^@]+)@([0-9a-fA-F]{40})$'){
            $errors.Add("$($workflow.Name):$($i+1): external action '$target' must be pinned to a full 40-character commit SHA.")
        }
    }
}


# Source/runtime security invariants. Keep these checks deterministic and high-signal:
# they run without sending source to a third-party scanner and block common credential
# leaks and security-boundary bypasses before code reaches a release.
$excludedPathPattern='\\(?:\.git|bin|obj|artifacts|release|BuildLogs|StartupLogs)\\'
$sourceExtensions=@('.cs','.py','.ps1','.psm1','.mjs','.js','.json','.yml','.yaml','.xml','.props','.targets','.config','.md','.bat','.cmd')
$securityFiles=@(
    Get-ChildItem -LiteralPath $Root -Recurse -File -ErrorAction SilentlyContinue |
        Where-Object {
            $extension=$_.Extension.ToLowerInvariant()
            if($sourceExtensions -notcontains $extension){return $false}
            if($_.Length -gt 2MB){return $false}
            $full=$_.FullName.Replace('/','\\')
            if($full -match $excludedPathPattern){return $false}
            return $true
        }
)

$secretPatterns=@(
    @{Name='GitHub classic token'; Pattern='(?<![A-Za-z0-9])gh[pousr]_[A-Za-z0-9]{20,}'},
    @{Name='GitHub fine-grained token'; Pattern='(?<![A-Za-z0-9])github_pat_[A-Za-z0-9_]{20,}'},
    @{Name='AWS access key id'; Pattern='(?<![A-Z0-9])AKIA[0-9A-Z]{16}(?![A-Z0-9])'},
    @{Name='private key material'; Pattern='-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----'}
)
foreach($file in $securityFiles){
    $text=Get-Content -LiteralPath $file.FullName -Raw -ErrorAction SilentlyContinue
    if($null -eq $text){continue}
    $relative=[IO.Path]::GetRelativePath($Root,$file.FullName).Replace('\\','/')
    foreach($rule in $secretPatterns){
        if($text -match $rule.Pattern){
            $errors.Add("${relative}: possible $($rule.Name) committed to the repository. Use an OS/GitHub secret store and rotate any exposed credential.")
        }
    }
}

$unsafePrimitiveRules=@(
    @{Glob='*.cs'; Name='TLS certificate validation bypass'; Pattern='DangerousAcceptAnyServerCertificateValidator|ServerCertificateCustomValidationCallback\s*=\s*[^;\r\n]*=>\s*true'},
    @{Glob='*.cs'; Name='unsafe legacy formatter'; Pattern='\b(?:BinaryFormatter|NetDataContractSerializer|LosFormatter)\b'},
    @{Glob='*.py'; Name='Python shell=True command execution'; Pattern='\bshell\s*=\s*True\b'},
    @{Glob='*.py'; Name='unsafe Python deserialization'; Pattern='\bpickle\.(?:loads?|Unpickler)\b'},
    @{Glob='*.py'; Name='dynamic Python code execution'; Pattern='(?m)^\s*(?:eval|exec)\s*\('}
)
foreach($rule in $unsafePrimitiveRules){
    foreach($file in @(Get-ChildItem -LiteralPath $Root -Recurse -File -Filter $rule.Glob -ErrorAction SilentlyContinue)){
        $full=$file.FullName
        $skip=$false
        if($full.Replace('/','\\') -match $excludedPathPattern){$skip=$true}
        if($skip -or $file.Length -gt 2MB){continue}
        $text=Get-Content -LiteralPath $full -Raw -ErrorAction SilentlyContinue
        if($null -ne $text -and $text -match $rule.Pattern){
            $relative=[IO.Path]::GetRelativePath($Root,$full).Replace('\\','/')
            $errors.Add("${relative}: forbidden security primitive detected ($($rule.Name)). Use the repository's fail-closed security helpers or document a narrowly reviewed exception in this policy.")
        }
    }
}

$updaterRoot=Join-Path $Root 'src\MhwModManager.Updater'
if(Test-Path -LiteralPath $updaterRoot){
    foreach($file in @(Get-ChildItem -LiteralPath $updaterRoot -Recurse -File -Filter '*.cs')){
        $text=Get-Content -LiteralPath $file.FullName -Raw
        $relative=[IO.Path]::GetRelativePath($Root,$file.FullName).Replace('\\','/')
        if($text -match '(?i)http://'){
            $errors.Add("${relative}: updater network code must not contain plaintext HTTP endpoints.")
        }
        if($text -match '\bZipFile\.ExtractToDirectory\s*\('){
            $errors.Add("${relative}: updater must use bounded path-safe extraction instead of ZipFile.ExtractToDirectory.")
        }
    }
}


$propsPath=Join-Path $Root 'Directory.Build.props'
if(!(Test-Path -LiteralPath $propsPath)){
    $errors.Add('Directory.Build.props is missing.')
}else{
    $props=Get-Content -LiteralPath $propsPath -Raw
    foreach($required in @(
        '<NuGetAudit>true</NuGetAudit>',
        '<NuGetAuditMode>all</NuGetAuditMode>',
        '<NuGetAuditLevel>low</NuGetAuditLevel>'
    )){
        if(-not $props.Contains($required)){
            $errors.Add("Directory.Build.props: required vulnerability-audit invariant is missing: $required")
        }
    }
    if($props -match '(?i)<NuGetAudit>\s*false\s*</NuGetAudit>'){
        $errors.Add('Directory.Build.props: NuGet vulnerability auditing must not be disabled.')
    }
}

$releasePath=Join-Path $workflowRoot 'windows-release-gate.yml'
if(!(Test-Path -LiteralPath $releasePath)){
    $errors.Add('windows-release-gate.yml is missing.')
}else{
    $release=Get-Content -LiteralPath $releasePath -Raw
    if($release.Contains('repos/cli/cli/releases/latest') -or $release.Contains('Get-Command gh')){
        $errors.Add('windows-release-gate.yml: privileged release tooling must not trust a moving latest release or arbitrary preinstalled gh.exe.')
    }
    foreach($required in @(
        '$version = ''2.101.0''',
        'bc6c814367b193cd8e713611d61e36013c0ef843b8f516458fe3eda039192794',
        'Get-FileHash',
        '$actualSha256 -ne $expectedSha256',
        'https://github.com/cli/cli/releases/download/v${version}/${assetName}'
    )){
        if(-not $release.Contains($required)){
            $errors.Add("windows-release-gate.yml: verified GitHub CLI bootstrap invariant missing: $required")
        }
    }
}

if($errors.Count -gt 0){
    Write-Host "CI security policy failed with $($errors.Count) violation(s):" -ForegroundColor Red
    foreach($item in $errors){Write-Host " - $item" -ForegroundColor Red}
    throw "CI security policy rejected the workflow set."
}

Write-Host "PASS: CI security policy ($($workflows.Count) workflows checked)." -ForegroundColor Green
