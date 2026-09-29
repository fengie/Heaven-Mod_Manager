param(
  [string]$ManifestPath='',
  [string]$ArtifactPath='',
  [string]$SourceRepository=$env:GITHUB_REPOSITORY,
  [string]$PublicRepository='fengie/mhw-mod-manager-releases'
)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest

$Root=(Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if([string]::IsNullOrWhiteSpace($ManifestPath)){
  $ManifestPath=Join-Path $Root 'artifacts\update-manifest.json'
}
$manifestFile=(Resolve-Path -LiteralPath $ManifestPath).Path
$manifest=Get-Content -LiteralPath $manifestFile -Raw | ConvertFrom-Json
if([string]::IsNullOrWhiteSpace($ArtifactPath)){
  $ArtifactPath=Join-Path (Split-Path -Parent $manifestFile) ([string]$manifest.artifactName)
}
$artifact=(Resolve-Path -LiteralPath $ArtifactPath).Path

if([string]::IsNullOrWhiteSpace($SourceRepository)){$SourceRepository='fengie/mhw-mods'}
if($SourceRepository -ne 'fengie/mhw-mods'){throw "Unexpected private source repository: $SourceRepository"}
if($PublicRepository -ne 'fengie/mhw-mod-manager-releases'){throw "Unexpected public updater repository: $PublicRepository"}
if([string]$manifest.channel -ne 'main'){throw "Refusing to mirror updater channel '$($manifest.channel)'."}
if([long]$manifest.buildNumber -le 0){throw 'Updater manifest build number must be positive.'}
if([string]$manifest.sourceSha -notmatch '^[0-9a-fA-F]{40}$'){throw 'Updater manifest source SHA must be a full 40-character Git SHA.'}

$expectedBuild=[long]$manifest.buildNumber
$expectedSource=([string]$manifest.sourceSha).ToLowerInvariant()
$tag="updater-main-$expectedBuild"

& (Join-Path $PSScriptRoot 'Test-UpdaterPackage.ps1') -ArtifactPath $artifact -ManifestPath $manifestFile -ExpectedSourceSha $expectedSource -ExpectedBuildNumber $expectedBuild

$sourceToken=[string]$env:GH_TOKEN
if([string]::IsNullOrWhiteSpace($sourceToken)){
  throw 'GH_TOKEN is required to verify the canonical private updater release before mirroring.'
}
$publicToken=[string]$env:MHW_PUBLIC_RELEASE_TOKEN
if([string]::IsNullOrWhiteSpace($publicToken)){
  Write-Host '::notice::Public updater mirror is not configured yet; set MHW_PUBLIC_RELEASE_TOKEN after creating fengie/mhw-mod-manager-releases.'
  exit 0
}

function New-GitHubHeaders {
  param([Parameter(Mandatory=$true)][string]$Token)
  return @{
    Accept='application/vnd.github+json'
    Authorization="Bearer $Token"
    'X-GitHub-Api-Version'='2022-11-28'
    'User-Agent'='mhw-mod-manager-public-updater-mirror'
  }
}

$sourceHeaders=New-GitHubHeaders -Token $sourceToken
$publicHeaders=New-GitHubHeaders -Token $publicToken

function Invoke-ReleaseApi {
  param(
    [Parameter(Mandatory=$true)][ValidateSet('GET','POST','PATCH','DELETE')][string]$Method,
    [Parameter(Mandatory=$true)][string]$Uri,
    [Parameter(Mandatory=$true)][hashtable]$Headers,
    [AllowNull()]$Body=$null,
    [switch]$AllowNotFound
  )
  try {
    $request=@{
      Method=$Method
      Uri=$Uri
      Headers=$Headers
      ErrorAction='Stop'
    }
    if($null -ne $Body){
      $request.ContentType='application/json'
      $request.Body=($Body | ConvertTo-Json -Depth 20 -Compress)
    }
    return Invoke-RestMethod @request
  }
  catch {
    $status=0
    try{$status=[int]$_.Exception.Response.StatusCode}catch{}
    if($AllowNotFound -and $status -eq 404){return $null}
    throw "GitHub release API $Method $Uri failed (HTTP $status): $($_.Exception.Message)"
  }
}

function Get-ReleaseByTag {
  param(
    [Parameter(Mandatory=$true)][string]$Repository,
    [Parameter(Mandatory=$true)][string]$Tag,
    [Parameter(Mandatory=$true)][hashtable]$Headers,
    [switch]$AllowNotFound
  )
  $escaped=[Uri]::EscapeDataString($Tag)
  return Invoke-ReleaseApi -Method GET -Uri "https://api.github.com/repos/$Repository/releases/tags/$escaped" -Headers $Headers -AllowNotFound:$AllowNotFound
}

function Get-ReleaseById {
  param(
    [Parameter(Mandatory=$true)][string]$Repository,
    [Parameter(Mandatory=$true)][long]$ReleaseId,
    [Parameter(Mandatory=$true)][hashtable]$Headers
  )
  return Invoke-ReleaseApi -Method GET -Uri "https://api.github.com/repos/$Repository/releases/$ReleaseId" -Headers $Headers
}

function Assert-ExactAssets {
  param(
    [Parameter(Mandatory=$true)]$Release,
    [Parameter(Mandatory=$true)][string]$Artifact,
    [Parameter(Mandatory=$true)][string]$ManifestFile,
    [Parameter(Mandatory=$true)]$Manifest
  )
  $assets=@($Release.assets)
  if($assets.Count -ne 2){throw "Updater release $($Release.tag_name) has $($assets.Count) assets; expected exactly 2."}
  $expected=@{
    ([string]$Manifest.artifactName)=(Get-Item -LiteralPath $Artifact)
    'update-manifest.json'=(Get-Item -LiteralPath $ManifestFile)
  }
  foreach($name in $expected.Keys){
    $asset=@($assets | Where-Object {[string]$_.name -eq $name})
    if($asset.Count -ne 1){throw "Updater release $($Release.tag_name) is missing exact asset '$name'."}
    if([long]$asset[0].size -ne [long]$expected[$name].Length){throw "Updater release asset '$name' size mismatch."}
    $digest=[string]$asset[0].digest
    if([string]::IsNullOrWhiteSpace($digest)){throw "Updater release asset '$name' has no server SHA-256 digest."}
    $expectedDigest='sha256:'+(Get-FileHash -LiteralPath $expected[$name].FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    if($digest -ne $expectedDigest){throw "Updater release asset '$name' digest mismatch."}
  }
}

function Send-ReleaseAsset {
  param(
    [Parameter(Mandatory=$true)][string]$Repository,
    [Parameter(Mandatory=$true)][long]$ReleaseId,
    [Parameter(Mandatory=$true)][string]$Path,
    [Parameter(Mandatory=$true)][hashtable]$Headers
  )
  $name=[Uri]::EscapeDataString((Split-Path -Leaf $Path))
  $uri="https://uploads.github.com/repos/$Repository/releases/$ReleaseId/assets?name=$name"
  try {
    return Invoke-RestMethod -Method POST -Uri $uri -Headers $Headers -InFile $Path -ContentType 'application/octet-stream' -ErrorAction Stop
  }
  catch {
    $status=0
    try{$status=[int]$_.Exception.Response.StatusCode}catch{}
    throw "GitHub release asset upload failed for $(Split-Path -Leaf $Path) (HTTP $status): $($_.Exception.Message)"
  }
}

function Remove-PublicDraftAndTag {
  param([long]$ReleaseId,[string]$Tag)
  if($ReleaseId -gt 0){
    try{
      $null=Invoke-ReleaseApi -Method DELETE -Uri "https://api.github.com/repos/$PublicRepository/releases/$ReleaseId" -Headers $publicHeaders
    }catch{Write-Host "::warning::Failed to delete public updater draft release: $($_.Exception.Message)"}
  }
  try{
    $escaped=[Uri]::EscapeDataString($Tag)
    $null=Invoke-ReleaseApi -Method DELETE -Uri "https://api.github.com/repos/$PublicRepository/git/refs/tags/$escaped" -Headers $publicHeaders -AllowNotFound
  }catch{Write-Host "::warning::Failed to delete public updater tag during cleanup: $($_.Exception.Message)"}
}

$canonical=Get-ReleaseByTag -Repository $SourceRepository -Tag $tag -Headers $sourceHeaders
if([bool]$canonical.draft -or [bool]$canonical.prerelease -or -not [bool]$canonical.immutable){
  throw "Canonical private updater release $tag is not a published immutable release."
}
if(([string]$canonical.target_commitish).ToLowerInvariant() -ne $expectedSource){
  throw "Canonical private updater release $tag targets $($canonical.target_commitish) instead of $expectedSource."
}
Assert-ExactAssets -Release $canonical -Artifact $artifact -ManifestFile $manifestFile -Manifest $manifest

$publicRepo=Invoke-ReleaseApi -Method GET -Uri "https://api.github.com/repos/$PublicRepository" -Headers $publicHeaders
if([bool]$publicRepo.private){throw "Public updater repository $PublicRepository is private; refusing to create a credential-dependent client feed."}

$existing=Get-ReleaseByTag -Repository $PublicRepository -Tag $tag -Headers $publicHeaders -AllowNotFound
if($null -ne $existing){
  if([bool]$existing.draft -or [bool]$existing.prerelease){throw "Public updater release $tag exists but is not published stable."}
  if(-not [bool]$existing.immutable){throw "Public updater release $tag exists but is not immutable."}
  Assert-ExactAssets -Release $existing -Artifact $artifact -ManifestFile $manifestFile -Manifest $manifest
  Write-Host "PASS: public updater mirror $tag already exists with exact immutable assets." -ForegroundColor Green
  exit 0
}

$notes=@(
  "Verified MHW Manual Mod Manager updater build $expectedBuild.",
  "Private source: $SourceRepository@$expectedSource",
  "Artifact SHA-256: $($manifest.sha256)",
  '',
  'This repository intentionally contains release assets only.'
) -join [Environment]::NewLine

$draftId=0L
try{
  $draft=Invoke-ReleaseApi -Method POST -Uri "https://api.github.com/repos/$PublicRepository/releases" -Headers $publicHeaders -Body @{
    tag_name=$tag
    target_commitish='main'
    name="MHW Manual Mod Manager updater build $expectedBuild"
    body=$notes
    draft=$true
    prerelease=$false
    make_latest='false'
  }
  $draftId=[long]$draft.id
  if($draftId -le 0){throw "Failed to create public updater draft release $tag with a valid release id."}

  $null=Send-ReleaseAsset -Repository $PublicRepository -ReleaseId $draftId -Path $artifact -Headers $publicHeaders
  $null=Send-ReleaseAsset -Repository $PublicRepository -ReleaseId $draftId -Path $manifestFile -Headers $publicHeaders

  $draftView=Get-ReleaseById -Repository $PublicRepository -ReleaseId $draftId -Headers $publicHeaders
  if(-not [bool]$draftView.draft -or [string]$draftView.tag_name -ne $tag){
    throw "Public updater draft release $tag changed state or identity before publication."
  }
  Assert-ExactAssets -Release $draftView -Artifact $artifact -ManifestFile $manifestFile -Manifest $manifest

  $null=Invoke-ReleaseApi -Method PATCH -Uri "https://api.github.com/repos/$PublicRepository/releases/$draftId" -Headers $publicHeaders -Body @{
    draft=$false
    make_latest='false'
  }

  $published=Get-ReleaseById -Repository $PublicRepository -ReleaseId $draftId -Headers $publicHeaders
  if([bool]$published.draft -or [bool]$published.prerelease -or [string]$published.tag_name -ne $tag){
    throw "Public updater release $tag has unexpected published state."
  }
  if(-not [bool]$published.immutable){
    throw "Public updater release $tag is not immutable. Enable immutable releases on $PublicRepository before using it as the client feed."
  }
  Assert-ExactAssets -Release $published -Artifact $artifact -ManifestFile $manifestFile -Manifest $manifest
  Write-Host "PASS: mirrored immutable updater release $tag to public feed $PublicRepository." -ForegroundColor Green
}
catch{
  if($draftId -gt 0){
    try{
      $current=Get-ReleaseById -Repository $PublicRepository -ReleaseId $draftId -Headers $publicHeaders
      if([bool]$current.draft -or -not [bool]$current.immutable){
        Remove-PublicDraftAndTag -ReleaseId $draftId -Tag $tag
      }
    }catch{
      Write-Host "::warning::Could not inspect/clean failed public updater mirror $($tag): $($_.Exception.Message)"
    }
  }
  throw
}
