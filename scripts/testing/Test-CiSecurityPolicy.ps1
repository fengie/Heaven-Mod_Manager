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

function Get-UnsafeWorkflowCredentialRecoveryViolations {
    param(
        [Parameter(Mandatory=$true)][string]$Text,
        [Parameter(Mandatory=$true)][string]$DisplayName
    )

    $violations=New-Object System.Collections.Generic.List[string]
    if($Text -match '(?im)\bgh(?:\.exe)?\s+auth\s+token\b'){
        $violations.Add("${DisplayName}: workflows must not recover machine-local GitHub CLI credentials.")
    }
    if(
        $Text -match '(?im)\bgit(?:\.exe)?\s+credential\s+fill\b' -or
        $Text -match '(?im)\.Arguments\s*=\s*[''\"]credential\s+fill[''\"]'
    ){
        $violations.Add("${DisplayName}: workflows must not recover machine-local Git Credential Manager credentials.")
    }
    if(
        $Text -match '(?i)MHW_PUBLIC_RELEASE_TOKEN' -and
        $Text -match '(?i)/actions/runners(?:/|\?|[''\"])'
    ){
        $violations.Add("${DisplayName}: MHW_PUBLIC_RELEASE_TOKEN must not be reused for Actions runner administration.")
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

# Regression for issue #627. Persistent self-hosted workflows must never
# scrape machine-local GitHub credentials or repurpose release credentials for
# cross-repository runner administration.
$unsafeGhCredentialFixture='gh auth token'
if(@(Get-UnsafeWorkflowCredentialRecoveryViolations -Text $unsafeGhCredentialFixture -DisplayName 'synthetic-gh-credential-fixture').Count -eq 0){
    $errors.Add('CI credential regression: gh auth token fixture was not rejected.')
}
$unsafeGitCredentialFixture=@'
$psi = New-Object System.Diagnostics.ProcessStartInfo
$psi.Arguments = 'credential fill'
'@
if(@(Get-UnsafeWorkflowCredentialRecoveryViolations -Text $unsafeGitCredentialFixture -DisplayName 'synthetic-git-credential-fixture').Count -eq 0){
    $errors.Add('CI credential regression: Git credential fill fixture was not rejected.')
}
$unsafeReleaseTokenReuseFixture=@'
MHW_PUBLIC_RELEASE_TOKEN: ${{ secrets.MHW_PUBLIC_RELEASE_TOKEN }}
Invoke-RestMethod -Method Post -Uri "https://api.github.com/repos/example/target/actions/runners/registration-token"
'@
if(@(Get-UnsafeWorkflowCredentialRecoveryViolations -Text $unsafeReleaseTokenReuseFixture -DisplayName 'synthetic-release-token-reuse-fixture').Count -eq 0){
    $errors.Add('CI credential regression: release-token runner-admin fixture was not rejected.')
}

foreach($workflow in @(Get-ChildItem -LiteralPath $workflowRoot -File | Where-Object { $_.Extension -in @('.yml','.yaml') })){
    $workflowText=Get-Content -LiteralPath $workflow.FullName -Raw
    foreach($violation in @(Get-UnsafeWorkflowTelemetryViolations -Text $workflowText -DisplayName $workflow.Name)){
        $errors.Add($violation)
    }
    foreach($violation in @(Get-UnsafeWorkflowCredentialRecoveryViolations -Text $workflowText -DisplayName $workflow.Name)){
        $errors.Add($violation)
    }
}

$persistentWriteAllowlist=@('windows-release-gate.yml','updater-installed-client-e2e.yml')

$updaterInstalledE2EPath=Join-Path $workflowRoot 'updater-installed-client-e2e.yml'
if(!(Test-Path -LiteralPath $updaterInstalledE2EPath -PathType Leaf)){
    $errors.Add('updater-installed-client-e2e.yml is missing.')
}else{
    $updaterInstalledE2E=Get-Content -LiteralPath $updaterInstalledE2EPath -Raw
    if($updaterInstalledE2E -match '(?i)\[skip ci\]'){
        $errors.Add('updater-installed-client-e2e.yml: evidence persistence must never bypass CI with a skip-ci commit marker.')
    }
    foreach($required in @(
        '$canonicalClosurePath = $closurePath.Replace',
        'updater-installed-client-e2e-v\d+\.\d+\.\d+\.log\z',
        'git diff --cached --name-only',
        'git diff --cached --name-status',
        '$expectedStatuses = @(',
        'git diff --cached --check',
        'Persist updater installed-client E2E evidence [evidence-only]',
        '$commitParent = (git rev-parse HEAD^).Trim()',
        '$commitParent -ne $persistenceBaseSha',
        'git diff-tree --no-commit-id --name-only -r HEAD',
        '$commitPaths.Count -ne 1'
    )){
        if(-not $updaterInstalledE2E.Contains($required)){
            $errors.Add("updater-installed-client-e2e.yml: evidence-only mutation boundary missing: $required")
        }
    }
}
foreach($workflowFile in @(Get-ChildItem -LiteralPath $workflowRoot -File -Filter '*.yml')){
    $workflow=Get-Content -LiteralPath $workflowFile.FullName -Raw
    if($workflow -notmatch '(?im)^\s*runs-on:\s*\[[^\]]*self-hosted[^\]]*\]'){continue}

    if($workflow -match '(?im)^\s*runs-on:\s*\[[^\]]*Windows[^\]]*mhw-mods[^\]]*\]' -and $workflow -match '(?im)^\s*shell:\s*pwsh\s*$'){
        $errors.Add("$($workflowFile.Name): Heaven Windows self-hosted jobs must use the supported Windows PowerShell shell unless pwsh is explicitly provisioned.")
    }

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

$workflowFeatureGatePath=Join-Path $workflowRoot 'workflow-feature-pr-gate.yml'
if(!(Test-Path -LiteralPath $workflowFeatureGatePath -PathType Leaf)){
    $errors.Add('workflow-feature-pr-gate.yml is missing.')
}else{
    $workflowFeatureGate=Get-Content -LiteralPath $workflowFeatureGatePath -Raw
    foreach($requiredPath in @("      - 'src/**'","      - 'tests/**'")){
        if(-not $workflowFeatureGate.Contains($requiredPath)){
            $errors.Add("workflow-feature-pr-gate.yml must cover all product source and test paths; missing $requiredPath")
        }
    }

    foreach($required in @(
        'admission:',
        'runs-on: [self-hosted, Windows, X64, mhw-mods]',
        'pull-requests: read',
        'Invoke-RestMethod -Uri $uri -Headers $headers -Method Get',
        'PR is explicitly marked superseded',
        'GitHub reports the current PR head as non-mergeable',
        'mergeable_state',
        'needs: admission'
    )){
        if(-not $workflowFeatureGate.Contains($required)){
            $errors.Add("workflow-feature-pr-gate.yml: stale/superseded PR admission invariant missing: $required")
        }
    }

    $admissionStart=$workflowFeatureGate.IndexOf('  admission:')
    $verifyStart=$workflowFeatureGate.IndexOf('  verify:')
    if($admissionStart -lt 0 -or $verifyStart -le $admissionStart){
        $errors.Add('workflow-feature-pr-gate.yml: admission job must precede the self-hosted verify job.')
    }else{
        $admissionBlock=$workflowFeatureGate.Substring($admissionStart,$verifyStart-$admissionStart)
        if($admissionBlock.Contains('actions/checkout@')){
            $errors.Add('workflow-feature-pr-gate.yml: admission must inspect GitHub PR metadata without checking out candidate code.')
        }
        foreach($forbidden in @('Verify-Release.ps1','dotnet restore','dotnet build','dotnet test')){
            if($admissionBlock.Contains($forbidden)){
                $errors.Add("workflow-feature-pr-gate.yml: admission must stay metadata-only and must not run expensive verification: $forbidden")
            }
        }
        if(-not $admissionBlock.Contains("if: github.event_name != 'pull_request' || github.event.pull_request.head.repo.full_name == github.repository")){
            $errors.Add('workflow-feature-pr-gate.yml: self-hosted admission must reject fork pull requests before runner execution.')
        }
    }

    foreach($required in @(
        'id: repository_verify',
        'id: restore',
        'if: ${{ !cancelled() }}',
        'id: build',
        'if: ${{ !cancelled() && steps.restore.outcome == ''success'' }}',
        'if: ${{ !cancelled() && steps.build.outcome == ''success'' }}'
    )){
        if(-not $workflowFeatureGate.Contains($required)){
            $errors.Add("workflow-feature-pr-gate.yml: diagnostic-continuation invariant missing: $required")
        }
    }

    $focusedSteps=@(
        'Run focused UX and XAML regressions',
        'Run legacy migration regression',
        'Run deployment concurrency regressions',
        'Run catalog sync regressions',
        'Run focused workflow regressions'
    )
    foreach($stepName in $focusedSteps){
        $stepIndex=$workflowFeatureGate.IndexOf("      - name: $stepName")
        if($stepIndex -lt 0){
            $errors.Add("workflow-feature-pr-gate.yml: focused diagnostic step missing: $stepName")
            continue
        }
        $nextIndex=$workflowFeatureGate.IndexOf('      - name:',$stepIndex+1)
        $stepBlock=if($nextIndex -gt $stepIndex){$workflowFeatureGate.Substring($stepIndex,$nextIndex-$stepIndex)}else{$workflowFeatureGate.Substring($stepIndex)}
        if(-not $stepBlock.Contains('if: ${{ !cancelled() && steps.build.outcome == ''success'' }}')){
            $errors.Add("workflow-feature-pr-gate.yml: focused diagnostic '$stepName' must continue after unrelated earlier failures when build prerequisites succeeded.")
        }
    }
}

$verifyReleasePath=Join-Path $Root 'scripts\release\Verify-Release.ps1'
$handoffGatePath=Join-Path $Root 'scripts\testing\Test-AgentHandoff.ps1'
if(!(Test-Path -LiteralPath $verifyReleasePath -PathType Leaf)){
    $errors.Add('Verify-Release.ps1 is missing.')
}else{
    $verifyRelease=Get-Content -LiteralPath $verifyReleasePath -Raw
    foreach($required in @(
        '[switch]$FeatureCandidate',
        '$handoffArgs=@{Root=$Root}',
        'if($FeatureCandidate){$handoffArgs.FeatureCandidate=$true}',
        '$negativeFixtureArgs=@{Root=$Root}',
        'if($FeatureCandidate){$negativeFixtureArgs.FeatureCandidate=$true}',
        'Test-AgentHandoff.ps1',
        'Test-AgentHandoff-NegativeFixtures.ps1'
    )){
        if(-not $verifyRelease.Contains($required)){
            $errors.Add("Verify-Release.ps1: feature-candidate verification routing invariant missing: $required")
        }
    }
}
$negativeFixtureGatePath=Join-Path $Root 'scripts\testing\Test-AgentHandoff-NegativeFixtures.ps1'
if(!(Test-Path -LiteralPath $negativeFixtureGatePath -PathType Leaf)){
    $errors.Add('Test-AgentHandoff-NegativeFixtures.ps1 is missing.')
}else{
    $negativeFixtureGate=Get-Content -LiteralPath $negativeFixtureGatePath -Raw
    foreach($required in @(
        '[switch]$FeatureCandidate',
        '$validationArgs=@{Root=$fixture}',
        'if($FeatureCandidate){$validationArgs.FeatureCandidate=$true}',
        'feature-candidate mode defers current-version handoff identity fixtures to canonical verification',
        'feature-candidate mode defers synthetic current-version closure projection fixtures to canonical verification'
    )){
        if(-not $negativeFixtureGate.Contains($required)){
            $errors.Add("Test-AgentHandoff-NegativeFixtures.ps1: feature-candidate propagation invariant missing: $required")
        }
    }
}

if(!(Test-Path -LiteralPath $handoffGatePath -PathType Leaf)){
    $errors.Add('Test-AgentHandoff.ps1 is missing.')
}else{
    $handoffGate=Get-Content -LiteralPath $handoffGatePath -Raw
    foreach($required in @(
        '[switch]$FeatureCandidate',
        'if(-not $FeatureCandidate){',
        'feature-candidate mode defers canonical release-version surface parity'
    )){
        if(-not $handoffGate.Contains($required)){
            $errors.Add("Test-AgentHandoff.ps1: feature-candidate/canonical separation invariant missing: $required")
        }
    }
}
if(Test-Path -LiteralPath $workflowFeatureGatePath -PathType Leaf){
    $workflowFeatureGate=Get-Content -LiteralPath $workflowFeatureGatePath -Raw
    if(-not $workflowFeatureGate.Contains('Verify-Release.ps1" -FeatureCandidate')){
        $errors.Add('workflow-feature-pr-gate.yml: feature PRs must invoke Verify-Release.ps1 with -FeatureCandidate.')
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
    if($release.Contains('-FeatureCandidate')){
        $errors.Add('windows-release-gate.yml: canonical main/release verification must never use feature-candidate metadata relaxation.')
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
    foreach($required in @(
        '.\scripts\release\Sync-VerificationContinuity.ps1',
        '_AGENT_CONTEXT/CURRENT_REVISION.json',
        '_AGENT_CONTEXT/CURRENT_STATE.md',
        'NEXT-AGENT-START-HERE.md'
    )){
        if(-not $release.Contains($required)){$errors.Add("windows-release-gate.yml: verification evidence/continuity atomicity invariant missing: $required")}
    }
    $syncIndex=$release.IndexOf('.\scripts\release\Sync-VerificationContinuity.ps1')
    if($syncIndex -ge 0){
        $postSync=$release.Substring($syncIndex)
        $postSyncHandoff=$postSync.IndexOf('.\scripts\testing\Test-AgentHandoff.ps1')
        $postSyncStage=$postSync.IndexOf("git add -- '.verification/function-status.json'")
        if($postSyncHandoff -lt 0 -or $postSyncStage -lt 0 -or $postSyncHandoff -ge $postSyncStage){
            $errors.Add('windows-release-gate.yml: synchronized continuity must pass the handoff validator before verification/evidence state is staged.')
        }
    }
    $publicPublishIndex=$release.IndexOf('.\scripts\release\Publish-PublicUpdaterRelease.ps1')
    $privatePublishIndex=$release.IndexOf('.\scripts\release\Publish-UpdaterRelease.ps1')
    $parityIndex=$release.IndexOf('Verify public and canonical updater release parity')
    if($publicPublishIndex -lt 0 -or $privatePublishIndex -lt 0 -or $publicPublishIndex -ge $privatePublishIndex){
        $errors.Add('windows-release-gate.yml: public updater client feed must publish before canonical/private release visibility.')
    }
    if($parityIndex -le $privatePublishIndex){$errors.Add('windows-release-gate.yml: public/private updater parity verification must run after both publication steps.')}
    $publicProvenanceIndex=$release.IndexOf('Reconcile public release provenance index')
    if($publicProvenanceIndex -le $parityIndex){$errors.Add('windows-release-gate.yml: public provenance reconciliation must run after public/private parity.')}
    if(-not $release.Contains('id: public_updater_release')){
        $errors.Add('windows-release-gate.yml: public updater publication must expose a step outcome for downstream gating.')
    }
    $publicReadyGuard="steps.public_updater_release.outputs.published == 'true'"
    if(([regex]::Matches($release,[regex]::Escape($publicReadyGuard))).Count -lt 2){
        $errors.Add('windows-release-gate.yml: canonical publication and public/private parity must both require a successful public updater publication outcome.')
    }
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
    foreach($required in @('ExpectedSourceSha=$env:GITHUB_SHA','OutcomePath=$env:GITHUB_OUTPUT','Write-UpdaterPublicationStepOutcome','-Published $false','-Published $true','Recovering abandoned public updater draft','Invoke-UpdaterDraftPublication','-RefreshMain','-EvaluateRefreshedMain','stale-main-unclassified-large-diff')){
        if(-not $text.Contains($required)){$errors.Add("Publish-PublicUpdaterRelease.ps1: updater transaction invariant missing: $required")}
    }
    if($text.Contains('Canonical private updater release')){$errors.Add('Publish-PublicUpdaterRelease.ps1: public client feed must not depend on an already-visible canonical/private release.')}
}

$publicProvenancePublisherPath=Join-Path $Root 'scripts\release\Publish-PublicReleaseProvenance.ps1'
if(!(Test-Path -LiteralPath $publicProvenancePublisherPath)){
    $errors.Add('Publish-PublicReleaseProvenance.ps1 is missing.')
}else{
    $text=Get-Content -LiteralPath $publicProvenancePublisherPath -Raw
    foreach($required in @(
        'fengie/mhw-mod-manager-release',
        'release-index.json',
        'Get-UpdaterReleasePages',
        'Add-PublicReleaseProvenanceRecordToIndexJson',
        'sha=[string]$indexFile.sha',
        "branch='main'",
        'MHW_PUBLIC_RELEASE_TOKEN'
    )){
        if(-not $text.Contains($required)){$errors.Add("Publish-PublicReleaseProvenance.ps1: provenance publication invariant missing: $required")}
    }
    if($text.Contains('Write-Host $publicToken') -or $text.Contains('Write-Output $publicToken')){
        $errors.Add('Publish-PublicReleaseProvenance.ps1: public release token must never be written to logs.')
    }
}

$sdkHelperPath=Join-Path $Root 'scripts\ci\Assert-PinnedDotNetSdk.ps1'
if(!(Test-Path -LiteralPath $sdkHelperPath)){
    $errors.Add('Assert-PinnedDotNetSdk.ps1 is missing.')
}else{
    $sdkHelper=Get-Content -LiteralPath $sdkHelperPath -Raw
    foreach($required in @('global.json','sdk.version','dotnet --version','does not match repository pin')){
        if(-not $sdkHelper.Contains($required)){$errors.Add("Assert-PinnedDotNetSdk.ps1: SDK authority invariant missing: $required")}
    }
}

function Get-SharedDotNetInstallRootViolations {
    param(
        [Parameter(Mandatory=$true)][string]$Text,
        [Parameter(Mandatory=$true)][string]$DisplayName
    )

    $violations=New-Object System.Collections.Generic.List[string]
    if($Text.Contains('Join-Path $env:RUNNER_TEMP ''dotnet''')){
        $violations.Add("${DisplayName}: repository SDK install root must be unique per run/attempt/job instead of sharing RUNNER_TEMP\\dotnet.")
    }
    return @($violations)
}

# Regression for #718: the former shared mutable SDK root must remain rejected
# even when the rest of the bootstrap contract is present.
$unsafeSharedSdkRootFixture=@'
actions/setup-dotnet@26b0ec14cb23fa6904739307f278c14f94c95bf1 # v5
global-json-file: global.json
Assert-PinnedDotNetSdk.ps1
$env:GITHUB_RUN_ID
$env:GITHUB_RUN_ATTEMPT
$env:GITHUB_JOB
Remove-Item -LiteralPath $installRoot -Recurse -Force
$installRoot=Join-Path $env:RUNNER_TEMP 'dotnet'
DOTNET_INSTALL_DIR=$installRoot
'@
$sharedRootFixtureViolations=@(Get-SharedDotNetInstallRootViolations -Text $unsafeSharedSdkRootFixture -DisplayName 'synthetic-shared-sdk-root-fixture')
if($sharedRootFixtureViolations.Count -ne 1 -or $sharedRootFixtureViolations[0] -notmatch 'must be unique'){
    $errors.Add('CI SDK bootstrap regression: shared RUNNER_TEMP\\dotnet fixture was not specifically rejected.')
}

$setupDotnetPin='actions/setup-dotnet@26b0ec14cb23fa6904739307f278c14f94c95bf1 # v5'
foreach($workflowName in @('windows-release-gate.yml','updater-publication-pr-gate.yml','updater-installed-client-e2e.yml','workflow-feature-pr-gate.yml')){
    $workflowPath=Join-Path $workflowRoot $workflowName
    if(!(Test-Path -LiteralPath $workflowPath)){
        $errors.Add("$workflowName is missing.")
        continue
    }
    $workflowText=Get-Content -LiteralPath $workflowPath -Raw
    foreach($required in @($setupDotnetPin,'global-json-file: global.json','Assert-PinnedDotNetSdk.ps1','DOTNET_INSTALL_DIR=','$env:GITHUB_RUN_ID','$env:GITHUB_RUN_ATTEMPT','$env:GITHUB_JOB','Remove-Item -LiteralPath $installRoot -Recurse -Force')){
        if(-not $workflowText.Contains($required)){$errors.Add("${workflowName}: repository SDK bootstrap invariant missing: $required")}
    }
    foreach($violation in @(Get-SharedDotNetInstallRootViolations -Text $workflowText -DisplayName $workflowName)){
        $errors.Add($violation)
    }
    if($workflowText.Contains("-ne '10.0.401'")){
        $errors.Add("${workflowName}: duplicated literal SDK-version comparison must defer to global.json.")
    }
}

if(Test-Path -LiteralPath $releasePath){
    if(-not $release.Contains('force_publish:')){$errors.Add('windows-release-gate.yml: manual force-publish input is missing.')}
    if(-not $release.Contains('id: release_intent')){$errors.Add('windows-release-gate.yml: semantic release-intent step is missing.')}
    if(-not $release.Contains('Get-UpdaterReleaseIntentDecision')){$errors.Add('windows-release-gate.yml: semantic release intent must use the shared updater policy.')}
    $releaseIntentStep=[regex]::Match($release,'(?ms)^[ \t]+- name: Resolve updater release intent[ \t]*\r?\n.*?(?=^[ \t]+- name: |\\z)')
    if(-not $releaseIntentStep.Success){
        $errors.Add('windows-release-gate.yml: semantic release-intent workflow step could not be isolated for pagination wiring validation.')
    }else{
        if(-not $releaseIntentStep.Value.Contains('Get-UpdaterReleasePages')){
            $errors.Add('windows-release-gate.yml: release-intent discovery must use the shared paginated release enumerator instead of assuming one API page is exhaustive.')
        }
        if(-not $releaseIntentStep.Value.Contains('per_page=$pageSize&page=$page')){
            $errors.Add('windows-release-gate.yml: release-intent pagination must pass explicit page size and page number to the GitHub releases API.')
        }
        if(-not $releaseIntentStep.Value.Contains('Get-UpdaterReleaseIntentDecision')){
            $errors.Add('windows-release-gate.yml: release-intent pagination output must feed the shared updater release-intent decision.')
        }
    }
    $intentGuard="steps.release_intent.outputs.publish == 'true'"
    if(([regex]::Matches($release,[regex]::Escape($intentGuard))).Count -lt 4){
        $errors.Add('windows-release-gate.yml: provenance and public/private/parity publication must require positive release intent.')
    }
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
