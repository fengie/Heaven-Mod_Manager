param([string]$Root = '')
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest

if([string]::IsNullOrWhiteSpace($Root)){
    $Root=(Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
}else{
    $Root=(Resolve-Path -LiteralPath $Root).Path
}

$errors=New-Object System.Collections.Generic.List[string]
$workflowRoot=Join-Path $Root '.github\workflows'

function Get-UnsafeWorkflowTelemetryViolations {
    param(
        [Parameter(Mandatory=$true)][string]$Text,
        [Parameter(Mandatory=$true)][string]$DisplayName
    )

    $violations=New-Object System.Collections.Generic.List[string]
    if(
        [regex]::IsMatch($Text,'(?is)\bWin32_Process\b.{0,4000}\bCommandLine\b') -or
        [regex]::IsMatch($Text,'(?is)\bCommandLine\b.{0,4000}\bWin32_Process\b')
    ){
        $violations.Add("${DisplayName}: workflows must not inspect or serialize Win32_Process.CommandLine.")
    }
    if($Text -match '(?i)worker-local-heartbeat\.json'){
        $violations.Add("${DisplayName}: workflows must not read the unrestricted Heaven Bridge local heartbeat object.")
    }
    return @($violations)
}

# Regression for issue #591. These synthetic values deliberately exercise secret
# spellings that blacklist-style redaction routinely misses. The policy blocks
# the source telemetry channel itself; the safe projection must contain no canary.
$telemetryCanaries=@(
    'MHW591_TOKEN_SPACE_C7C8A1',
    'MHW591_TOKEN_EQUALS_F908D2',
    'MHW591_QUOTED_PASSWORD_41EA33',
    'MHW591_BEARER_74321B',
    'MHW591_URL_CREDENTIAL_A02D11',
    'MHW591_FUTURE_SECRET_ARG_6B1E57',
    'MHW591_HEARTBEAT_SECRET_0C9D44'
)
$unsafeProcessFixture=@"
Get-CimInstance Win32_Process |
  Select-Object ProcessId,Name,CommandLine
# synthetic argv only; values are never emitted:
--token $($telemetryCanaries[0])
--token=$($telemetryCanaries[1])
--password "$($telemetryCanaries[2])"
Authorization: Bearer $($telemetryCanaries[3])
https://user:$($telemetryCanaries[4])@example.invalid/
--future-secret-option $($telemetryCanaries[5])
"@
if(@(Get-UnsafeWorkflowTelemetryViolations -Text $unsafeProcessFixture -DisplayName 'synthetic-process-fixture').Count -eq 0){
    $errors.Add('CI telemetry regression: Win32_Process.CommandLine fixture was not rejected.')
}

$unsafeHeartbeatFixture=@"
`$localHeartbeatPath = Join-Path `$env:USERPROFILE 'HeavenBridge\worker-local-heartbeat.json'
`$local = Get-Content -LiteralPath `$localHeartbeatPath -Raw | ConvertFrom-Json
`$local | Add-Member -NotePropertyName FutureSecret -NotePropertyValue '$($telemetryCanaries[6])'
Write-Host ("LOCAL_HEARTBEAT=" + (`$local | ConvertTo-Json -Compress -Depth 5))
"@
if(@(Get-UnsafeWorkflowTelemetryViolations -Text $unsafeHeartbeatFixture -DisplayName 'synthetic-heartbeat-fixture').Count -eq 0){
    $errors.Add('CI telemetry regression: unrestricted local heartbeat fixture was not rejected.')
}

$safeTelemetry=[ordered]@{
    executable='python.exe'
    classification='heaven-bridge-worker'
    running=$true
} | ConvertTo-Json -Compress
foreach($canary in $telemetryCanaries){
    if($safeTelemetry.Contains($canary)){
        $errors.Add('CI telemetry regression: allowlisted telemetry exposed a synthetic canary.')
        break
    }
}

foreach($workflow in @(Get-ChildItem -LiteralPath $workflowRoot -File | Where-Object { $_.Extension -in @('.yml','.yaml') })){
    $workflowText=Get-Content -LiteralPath $workflow.FullName -Raw
    foreach($violation in @(Get-UnsafeWorkflowTelemetryViolations -Text $workflowText -DisplayName $workflow.Name)){
        $errors.Add($violation)
    }
}

$persistentWriteAllowlist=@('windows-release-gate.yml','updater-installed-client-e2e.yml')
foreach($workflowFile in @(Get-ChildItem -LiteralPath $workflowRoot -File -Filter '*.yml')){
    $workflow=Get-Content -LiteralPath $workflowFile.FullName -Raw
    if($workflow -notmatch '(?im)^\s*runs-on:\s*\[[^\]]*self-hosted[^\]]*\]'){continue}

    $checkoutCount=[regex]::Matches($workflow,'(?im)^\s*uses:\s*actions/checkout@').Count
    $noPersistCount=[regex]::Matches($workflow,'(?im)^\s*persist-credentials:\s*false\s*$').Count
    if($checkoutCount -ne $noPersistCount){
        $errors.Add("$($workflowFile.Name): every checkout on a persistent self-hosted runner must set persist-credentials: false.")
    }

    $hasPullRequestTrigger=[regex]::IsMatch($workflow,'(?m)^\s{0,2}pull_request\s*:')
    $hasSameRepoGuard=$workflow -match 'github\.event\.pull_request\.head\.repo\.full_name\s*==\s*github\.repository'
    if($hasPullRequestTrigger -and -not $hasSameRepoGuard){
        $errors.Add("$($workflowFile.Name): self-hosted pull-request execution must reject fork PR code with an exact head.repo.full_name == github.repository guard.")
    }

    $jobsMatch=[regex]::Match($workflow,'(?m)^jobs:\s*$')
    $header=if($jobsMatch.Success){$workflow.Substring(0,$jobsMatch.Index)}else{$workflow}
    if($header -notmatch '(?m)^permissions:\s*$'){
        $errors.Add("$($workflowFile.Name): persistent self-hosted workflows must declare explicit top-level permissions.")
    }
    $topLevelWrites=[regex]::Matches($header,'(?m)^\s{2,}[A-Za-z0-9_-]+:\s*write\s*$')
    if($topLevelWrites.Count -gt 0 -and $persistentWriteAllowlist -notcontains $workflowFile.Name){
        $errors.Add("$($workflowFile.Name): persistent self-hosted workflow has top-level write permission but is not an approved mutation/release workflow.")
    }

    foreach($match in [regex]::Matches($workflow,'(?im)^\s*uses:\s*([^\.\s][^@\s]+)@([^\s#]+)')){
        $action=[string]$match.Groups[1].Value
        $revision=[string]$match.Groups[2].Value
        if($revision -notmatch '^[0-9a-fA-F]{40}$'){
            $errors.Add("$($workflowFile.Name): external action $action must be pinned to a full 40-character commit SHA; found '$revision'.")
        }
    }
}

$trackedSecretGate=Join-Path $Root 'scripts\testing\Test-TrackedSecretLeaks.ps1'
if(!(Test-Path -LiteralPath $trackedSecretGate -PathType Leaf)){
    $errors.Add('Test-TrackedSecretLeaks.ps1 is missing.')
}else{
    try{ & $trackedSecretGate -Root $Root }
    catch{ $errors.Add("Tracked secret/private-key gate failed: $($_.Exception.Message)") }
}
$propsPath=Join-Path $Root 'Directory.Build.props'
if(!(Test-Path -LiteralPath $propsPath)){
    $errors.Add('Directory.Build.props is missing.')
}else{
    $props=Get-Content -LiteralPath $propsPath -Raw
    foreach($required in @('<NuGetAudit>true</NuGetAudit>','<NuGetAuditMode>all</NuGetAuditMode>','<NuGetAuditLevel>low</NuGetAuditLevel>')){
        if(-not $props.Contains($required)){$errors.Add("Directory.Build.props: required vulnerability-audit invariant is missing: $required")}
    }
    if($props -match '(?i)<NuGetAudit>\s*false\s*</NuGetAudit>'){$errors.Add('Directory.Build.props: NuGet vulnerability auditing must not be disabled.')}
}

$updaterRoot=Join-Path $Root 'src\MhwModManager.Updater'
if(Test-Path -LiteralPath $updaterRoot){
    foreach($file in @(Get-ChildItem -LiteralPath $updaterRoot -Recurse -File -Filter '*.cs')){
        $text=Get-Content -LiteralPath $file.FullName -Raw
        $relative=$file.FullName.Substring($Root.Length).TrimStart([char[]]'\/').Replace('\','/')
        if($text -match '(?i)http://'){$errors.Add("PATH: possible plaintext HTTP updater endpoint: $relative")}
        if($text -match '\bZipFile\.ExtractToDirectory\s*\('){$errors.Add("PATH: updater must use bounded path-safe extraction: $relative")}
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
        if(-not $release.Contains($required)){$errors.Add("windows-release-gate.yml: verified GitHub CLI bootstrap invariant missing: $required")}
    }
    $publicPublishIndex=$release.IndexOf('.\scripts\release\Publish-PublicUpdaterRelease.ps1')
    $privatePublishIndex=$release.IndexOf('.\scripts\release\Publish-UpdaterRelease.ps1')
    $parityIndex=$release.IndexOf('Verify public and canonical updater release parity')
    if($publicPublishIndex -lt 0 -or $privatePublishIndex -lt 0 -or $publicPublishIndex -ge $privatePublishIndex){
        $errors.Add('windows-release-gate.yml: public updater client feed must publish before canonical/private release visibility.')
    }
    if($parityIndex -le $privatePublishIndex){$errors.Add('windows-release-gate.yml: public/private updater parity verification must run after both publication steps.')}
    if(-not [regex]::IsMatch($release,'group:\s*windows-release-main\s+cancel-in-progress:\s*false')){
        $errors.Add('windows-release-gate.yml: cross-repository updater publication must not be cancelled in progress.')
    }
}

$privatePublisherPath=Join-Path $Root 'scripts\release\Publish-UpdaterRelease.ps1'
if(!(Test-Path -LiteralPath $privatePublisherPath)){
    $errors.Add('Publish-UpdaterRelease.ps1 is missing.')
}else{
    $text=Get-Content -LiteralPath $privatePublisherPath -Raw
    foreach($required in @(
        "publicRepository='fengie/mhw-mod-manager-release'",
        'Public updater client feed $tag must be published before canonical updater release publication.',
        'Assert-UpdaterReleaseAssets -Release $publicRelease'
    )){
        if(-not $text.Contains($required)){$errors.Add("Publish-UpdaterRelease.ps1: canonical publisher precondition missing: $required")}
    }
}

$publicPublisherPath=Join-Path $Root 'scripts\release\Publish-PublicUpdaterRelease.ps1'
if(!(Test-Path -LiteralPath $publicPublisherPath)){
    $errors.Add('Publish-PublicUpdaterRelease.ps1 is missing.')
}else{
    $text=Get-Content -LiteralPath $publicPublisherPath -Raw
    foreach($required in @('ExpectedSourceSha=$env:GITHUB_SHA','Recovering abandoned public updater draft','Invoke-UpdaterDraftPublication','-RefreshMain','-EvaluateRefreshedMain','stale-main-unclassified-large-diff')){
        if(-not $text.Contains($required)){$errors.Add("Publish-PublicUpdaterRelease.ps1: updater transaction invariant missing: $required")}
    }
    if($text.Contains('Canonical private updater release')){$errors.Add('Publish-PublicUpdaterRelease.ps1: public client feed must not depend on an already-visible canonical/private release.')}
}

$updaterPrGatePath=Join-Path $workflowRoot 'updater-publication-pr-gate.yml'
if(Test-Path -LiteralPath $updaterPrGatePath){
    $gate=Get-Content -LiteralPath $updaterPrGatePath -Raw
    if(-not $gate.Contains("      - '.github/workflows/windows-release-gate.yml'")){
        $errors.Add('updater-publication-pr-gate.yml: release workflow ordering changes must trigger the updater publication PR gate.')
    }
}

if($errors.Count -gt 0){
    Write-Host "MHW product security policy failed with $($errors.Count) violation(s):" -ForegroundColor Red
    foreach($item in $errors){Write-Host " - $item" -ForegroundColor Red}
    throw 'MHW product security policy rejected the source tree.'
}
Write-Host 'PASS: MHW product/update/release security policy.' -ForegroundColor Green
