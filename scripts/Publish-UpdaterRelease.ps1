param(
  [string]$ManifestPath='',
  [string]$ArtifactPath='',
  [string]$Repository=$env:GITHUB_REPOSITORY,
  [string]$ExpectedSourceSha=$env:GITHUB_SHA,
  [long]$ExpectedBuildNumber=0
)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$Root=(Resolve-Path (Join-Path $PSScriptRoot '..')).Path
. (Join-Path $PSScriptRoot 'UpdaterReleasePolicy.ps1')
. (Join-Path $PSScriptRoot 'UpdaterReleasePublication.ps1')

if([string]::IsNullOrWhiteSpace($ManifestPath)){
  $ManifestPath=Join-Path $Root 'artifacts\update-manifest.json'
}
$manifestFile=(Resolve-Path -LiteralPath $ManifestPath).Path
$manifest=Get-Content -LiteralPath $manifestFile -Raw | ConvertFrom-Json
if([string]::IsNullOrWhiteSpace($ArtifactPath)){
  $ArtifactPath=Join-Path (Split-Path -Parent $manifestFile) ([string]$manifest.artifactName)
}
$artifact=(Resolve-Path -LiteralPath $ArtifactPath).Path

if([string]::IsNullOrWhiteSpace($Repository)){$Repository='fengie/mhw-mods'}
if($Repository -ne 'fengie/mhw-mods'){throw "Unexpected updater repository: $Repository"}
if([string]::IsNullOrWhiteSpace($ExpectedSourceSha)){$ExpectedSourceSha=[string]$manifest.sourceSha}
if($ExpectedBuildNumber -le 0){
  $runNumber=$env:GITHUB_RUN_NUMBER
  if(-not [string]::IsNullOrWhiteSpace($runNumber)){
    [long]$parsed=0
    if(-not [long]::TryParse($runNumber,[ref]$parsed)){throw "Invalid GITHUB_RUN_NUMBER: $runNumber"}
    $ExpectedBuildNumber=$parsed
  } else {
    $ExpectedBuildNumber=[long]$manifest.buildNumber
  }
}
if([string]$manifest.sourceSha -ne $ExpectedSourceSha){throw 'Update manifest source SHA does not match the exact workflow source.'}
if([long]$manifest.buildNumber -ne $ExpectedBuildNumber){throw 'Update manifest build number does not match the exact workflow build.'}
if([string]$manifest.channel -ne 'main'){throw "Refusing to publish updater channel '$($manifest.channel)'."}
& (Join-Path $PSScriptRoot 'Test-UpdaterPackage.ps1') -ArtifactPath $artifact -ManifestPath $manifestFile -ExpectedSourceSha $ExpectedSourceSha -ExpectedBuildNumber $ExpectedBuildNumber

function Assert-UpdaterReleaseAssets {
  param($Release,[string]$Artifact,[string]$ManifestFile,$Manifest)
  $assets=@($Release.assets)
  if($assets.Count -ne 2){throw "Updater release has $($assets.Count) assets; expected exactly 2."}
  $expected=@{
    ([string]$Manifest.artifactName)=(Get-Item -LiteralPath $Artifact)
    'update-manifest.json'=(Get-Item -LiteralPath $ManifestFile)
  }
  foreach($name in $expected.Keys){
    $asset=@($assets | Where-Object {[string]$_.name -eq $name})
    if($asset.Count -ne 1){throw "Updater release is missing exact asset '$name'."}
    if([long]$asset[0].size -ne [long]$expected[$name].Length){throw "Updater release asset '$name' size mismatch."}
    $digest=[string]$asset[0].digest
    if([string]::IsNullOrWhiteSpace($digest)){throw "Updater release asset '$name' has no server SHA-256 digest."}
    $expectedDigest='sha256:'+(Get-FileHash -LiteralPath $expected[$name].FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    if($digest -ne $expectedDigest){throw "Updater release asset '$name' digest mismatch."}
  }
}

if([string]::IsNullOrWhiteSpace($env:GH_TOKEN)){throw 'GH_TOKEN is required for updater publication.'}

$GitHubHeaders=@{
  Authorization='Bearer '+$env:GH_TOKEN
  Accept='application/vnd.github+json'
  'X-GitHub-Api-Version'='2022-11-28'
  'User-Agent'='mhw-mods-updater-release'
}
$GitHubApiBase="https://api.github.com/repos/$Repository"

function Invoke-GitHubJson {
  param(
    [Parameter(Mandatory=$true)][ValidateSet('GET','POST','PATCH','DELETE')][string]$Method,
    [Parameter(Mandatory=$true)][string]$Path,
    $Body=$null
  )
  $uri=$GitHubApiBase+'/'+$Path.TrimStart('/')
  $params=@{
    Uri=$uri
    Method=$Method
    Headers=$GitHubHeaders
    ErrorAction='Stop'
  }
  if($null -ne $Body){
    $params['Body']=$Body | ConvertTo-Json -Depth 20 -Compress
    $params['ContentType']='application/json'
  }
  return Invoke-RestMethod @params
}

function Get-GitHubReleaseImmutable {
  param($Release)
  $property=$Release.PSObject.Properties['immutable']
  if($null -eq $property){return $false}
  return [bool]$property.Value
}

function Get-GitHubReleases {
  $all=New-Object System.Collections.Generic.List[object]
  for($page=1;$page -le 20;$page++){
    $batch=@(Invoke-GitHubJson -Method GET -Path "releases?per_page=100&page=$page")
    foreach($item in $batch){if($null -ne $item){$all.Add($item)}}
    if($batch.Count -lt 100){break}
  }
  return @($all)
}

function Get-GitHubReleaseByTag {
  param([Parameter(Mandatory=$true)][string]$Tag)
  $encoded=[uri]::EscapeDataString($Tag)
  return Invoke-GitHubJson -Method GET -Path "releases/tags/$encoded"
}

function Get-GitHubTagRef {
  param([Parameter(Mandatory=$true)][string]$Tag)
  $encoded=[uri]::EscapeDataString($Tag)
  return Invoke-GitHubJson -Method GET -Path "git/ref/tags/$encoded"
}

function Upload-GitHubReleaseAsset {
  param(
    [Parameter(Mandatory=$true)][long]$ReleaseId,
    [Parameter(Mandatory=$true)][string]$Path,
    [Parameter(Mandatory=$true)][string]$ContentType
  )
  $name=[uri]::EscapeDataString([IO.Path]::GetFileName($Path))
  $uri="https://uploads.github.com/repos/$Repository/releases/$ReleaseId/assets?name=$name"
  $response=Invoke-WebRequest -UseBasicParsing -Uri $uri -Method POST -Headers $GitHubHeaders -InFile $Path -ContentType $ContentType -ErrorAction Stop
  if([string]::IsNullOrWhiteSpace([string]$response.Content)){throw "GitHub returned no response while uploading $Path."}
  return $response.Content | ConvertFrom-Json
}

function Remove-GitHubTagIfPresent {
  param([Parameter(Mandatory=$true)][string]$Tag)
  $encoded=[uri]::EscapeDataString($Tag)
  try {
    $null=Invoke-GitHubJson -Method DELETE -Path "git/refs/tags/$encoded"
  }
  catch {
    $response=$_.Exception.Response
    if($null -ne $response -and [int]$response.StatusCode -eq 404){return}
    throw
  }
}

Push-Location $Root
try {
  & git fetch origin main --tags
  if($LASTEXITCODE -ne 0){throw 'Failed to fetch current main/tags before updater publication.'}
  $remoteMain=(& git rev-parse origin/main).Trim()
  $remoteMainChangedPaths=@()
  if(-not [string]::Equals($remoteMain,$ExpectedSourceSha,[StringComparison]::OrdinalIgnoreCase)){
    & git merge-base --is-ancestor $ExpectedSourceSha $remoteMain
    if($LASTEXITCODE -ne 0){
      Write-Host "::notice::Skipping updater publication: stale-main-non-descendant."
      exit 0
    }
    $remoteMainChangedPaths=@(& git diff --name-only "$ExpectedSourceSha..$remoteMain" --)
    if($LASTEXITCODE -ne 0){throw 'Failed to classify current main drift for updater publication.'}
  }
  $tag="updater-main-$ExpectedBuildNumber"

  $apiReleases=@(Get-GitHubReleases)
  $normalizedReleases=@($apiReleases | ForEach-Object {
    [pscustomobject]@{
      tagName=[string]$_.tag_name
      isDraft=[bool]$_.draft
      isImmutable=(Get-GitHubReleaseImmutable $_)
    }
  })
  $releaseJson=ConvertTo-Json -InputObject @($normalizedReleases) -Depth 5 -Compress
  $releaseList=ConvertFrom-UpdaterReleaseList -Json $releaseJson
  $releases=$releaseList.Releases
  $existing=@($releases | Where-Object {[string]$_.tagName -eq $tag})

  if($existing.Count -gt 0){
    if($existing.Count -ne 1){throw "Multiple releases unexpectedly use tag $tag."}
    if([bool]$existing[0].isDraft){throw "Updater release $tag exists only as a draft; refusing to overwrite or publish it automatically."}
    if(-not [bool]$existing[0].isImmutable){throw "Updater release $tag exists but is not immutable; refusing to trust it as an update feed."}
    $existingRef=Get-GitHubTagRef -Tag $tag
    $existingRefJson=$existingRef | ConvertTo-Json -Depth 10 -Compress
    $tagSha=Get-UpdaterTagCommitFromRefJson -Json $existingRefJson -ExpectedTag $tag
    if($tagSha -ne $ExpectedSourceSha){throw "Existing updater release $tag points to $tagSha instead of $ExpectedSourceSha."}

    $release=Get-GitHubReleaseByTag -Tag $tag
    if([bool]$release.draft){throw "Existing updater release $tag unexpectedly became a draft."}
    Assert-UpdaterReleaseAssets -Release $release -Artifact $artifact -ManifestFile $manifestFile -Manifest $manifest
    Write-Host "PASS: updater release $tag already exists with exact immutable assets for source $ExpectedSourceSha." -ForegroundColor Green
    exit 0
  }

  & git show-ref --verify --quiet "refs/tags/$tag"
  if($LASTEXITCODE -eq 0){throw "Tag $tag exists without a published release; refusing to reuse or overwrite it."}

  $published=New-Object System.Collections.Generic.List[object]
  foreach($item in $releases){
    if([bool]$item.isDraft){continue}
    $build=Get-UpdaterBuildFromTag ([string]$item.tagName)
    if($build -gt 0){$published.Add([pscustomobject]@{Build=$build;Tag=[string]$item.tagName})}
  }
  $previous=@($published | Sort-Object Build -Descending | Select-Object -First 1)
  $previousBuild=0L
  $previousSha=''
  $changed=@()

  if($previous.Count -gt 0){
    $previousBuild=[long]$previous[0].Build
    $previousTag=[string]$previous[0].Tag
    & git show-ref --verify --quiet "refs/tags/$previousTag"
    if($LASTEXITCODE -ne 0){throw "Latest updater release tag $previousTag was not fetched."}
    $previousSha=(& git rev-list -n 1 "refs/tags/$previousTag").Trim()
    & git merge-base --is-ancestor $previousSha $ExpectedSourceSha
    if($LASTEXITCODE -ne 0){throw "Latest updater release source $previousSha is not an ancestor of $ExpectedSourceSha."}
    $changed=@(& git diff --name-only "$previousSha..$ExpectedSourceSha" --)
    if($LASTEXITCODE -ne 0){throw 'Failed to determine updater release-input changes.'}
  }

  $decision=Get-UpdaterPublicationDecision -CurrentBuild $ExpectedBuildNumber -CurrentSourceSha $ExpectedSourceSha -RemoteMainSha $remoteMain -PreviousBuild $previousBuild -PreviousSourceSha $previousSha -ChangedPaths $changed -RemoteMainChangedPaths $remoteMainChangedPaths
  if(-not $decision.Publish){
    Write-Host "::notice::Skipping updater publication: $($decision.Reason)."
    exit 0
  }

  $notes=@(
    "Verified automatic-updater build $ExpectedBuildNumber.",
    "Source: $ExpectedSourceSha",
    "Artifact SHA-256: $($manifest.sha256)"
  ) -join [Environment]::NewLine

  $script:UpdaterDraftReleaseId=0L
  $publicationArgs=@{
    ExpectedSourceSha=$ExpectedSourceSha
    CreateDraft={
      $created=Invoke-GitHubJson -Method POST -Path 'releases' -Body @{
        tag_name=$tag
        target_commitish=$ExpectedSourceSha
        name="MHW Manual Mod Manager updater build $ExpectedBuildNumber"
        body=$notes
        draft=$true
        prerelease=$false
        make_latest='false'
      }
      $script:UpdaterDraftReleaseId=[long]$created.id
      if($script:UpdaterDraftReleaseId -le 0){throw "Updater draft release $tag returned no valid release id."}
      if(-not [bool]$created.draft -or [string]$created.tag_name -ne $tag){throw "Updater draft release $tag has unexpected identity/draft state."}
    }
    UploadAssets={
      if($script:UpdaterDraftReleaseId -le 0){throw 'Updater draft release id is unavailable for asset upload.'}
      $null=Upload-GitHubReleaseAsset -ReleaseId $script:UpdaterDraftReleaseId -Path $artifact -ContentType 'application/zip'
      $null=Upload-GitHubReleaseAsset -ReleaseId $script:UpdaterDraftReleaseId -Path $manifestFile -ContentType 'application/json'
    }
    VerifyDraft={
      if($script:UpdaterDraftReleaseId -le 0){throw 'Updater draft release id is unavailable for verification.'}
      $draftRelease=Invoke-GitHubJson -Method GET -Path "releases/$script:UpdaterDraftReleaseId"
      if(-not [bool]$draftRelease.draft -or [string]$draftRelease.tag_name -ne $tag){
        throw "Updater draft release $tag changed state or identity before publication."
      }
      Assert-UpdaterReleaseAssets -Release $draftRelease -Artifact $artifact -ManifestFile $manifestFile -Manifest $manifest
    }
    RefreshMain={
      & git fetch origin main
      if($LASTEXITCODE -ne 0){throw 'Failed to refresh origin/main after updater asset upload.'}
      return (& git rev-parse origin/main).Trim()
    }
    DeleteDraft={
      if($script:UpdaterDraftReleaseId -le 0){throw 'Updater draft release id is unavailable for cleanup.'}
      $null=Invoke-GitHubJson -Method DELETE -Path "releases/$script:UpdaterDraftReleaseId"
      Remove-GitHubTagIfPresent -Tag $tag
      $script:UpdaterDraftReleaseId=0L
    }
    PublishDraft={
      if($script:UpdaterDraftReleaseId -le 0){throw 'Updater draft release id is unavailable for publication.'}
      $publishedDraft=Invoke-GitHubJson -Method PATCH -Path "releases/$script:UpdaterDraftReleaseId" -Body @{
        draft=$false
        make_latest='false'
      }
      if([bool]$publishedDraft.draft -or [string]$publishedDraft.tag_name -ne $tag){
        throw "Updater release $tag did not leave draft state after publication."
      }
    }
    EvaluateRefreshedMain={
      param([string]$refreshedMain)
      & git merge-base --is-ancestor $ExpectedSourceSha $refreshedMain
      if($LASTEXITCODE -ne 0){
        return [pscustomobject]@{Publish=$false;Reason='stale-main-non-descendant'}
      }
      $driftPaths=@(& git diff --name-only "$ExpectedSourceSha..$refreshedMain" --)
      if($LASTEXITCODE -ne 0){throw 'Failed to classify post-upload main drift for updater publication.'}
      return Get-UpdaterMainDriftDecision -CurrentSourceSha $ExpectedSourceSha -RemoteMainSha $refreshedMain -ChangedPaths $driftPaths
    }
  }
  $publication=Invoke-UpdaterDraftPublication @publicationArgs

  if(-not $publication.Published){
    Write-Host "::notice::Skipping updater publication: $($publication.Reason) (remote main $($publication.RemoteMainSha))."
    exit 0
  }

  $releaseView=Get-GitHubReleaseByTag -Tag $tag
  if([string]$releaseView.tag_name -ne $tag -or [bool]$releaseView.draft){
    throw "Published updater release $tag has unexpected identity/draft state."
  }
  if(-not (Get-GitHubReleaseImmutable $releaseView)){
    Write-Host "::error::GitHub published $tag without immutable-release protection; attempting to withdraw the invalid updater feed."
    try {
      $null=Invoke-GitHubJson -Method DELETE -Path "releases/$([long]$releaseView.id)"
      Remove-GitHubTagIfPresent -Tag $tag
    }
    catch {
      Write-Host "::error::Failed to withdraw non-immutable updater release $($tag): $($_.Exception.Message)"
    }
    throw "Updater release $tag was not immutable and is not accepted as a safe publication."
  }

  $publishedRef=Get-GitHubTagRef -Tag $tag
  $publishedRefJson=$publishedRef | ConvertTo-Json -Depth 10 -Compress
  $publishedSha=Get-UpdaterTagCommitFromRefJson -Json $publishedRefJson -ExpectedTag $tag
  if($publishedSha -ne $ExpectedSourceSha){throw "Published updater tag $tag points to $publishedSha instead of $ExpectedSourceSha."}

  $publishedRelease=Get-GitHubReleaseByTag -Tag $tag
  if([bool]$publishedRelease.draft){throw "Updater release $tag remained a draft after publication."}
  Assert-UpdaterReleaseAssets -Release $publishedRelease -Artifact $artifact -ManifestFile $manifestFile -Manifest $manifest
  Write-Host "PASS: published immutable updater release $tag for exact source $ExpectedSourceSha." -ForegroundColor Green
}
finally {
  Pop-Location
}
