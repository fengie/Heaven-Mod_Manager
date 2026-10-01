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
