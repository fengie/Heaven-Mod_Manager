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

if(-not (Get-Command gh -ErrorAction SilentlyContinue)){throw 'GitHub CLI (gh) is required for updater publication.'}
if([string]::IsNullOrWhiteSpace($env:GH_TOKEN)){throw 'GH_TOKEN is required for updater publication.'}

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

  $releaseOutput=@(& gh release list --repo $Repository --limit 1000 --json tagName,isDraft,isImmutable)
  if($LASTEXITCODE -ne 0){throw 'Failed to list existing GitHub releases.'}
  $releaseJson=$releaseOutput -join [Environment]::NewLine
  $releaseList=ConvertFrom-UpdaterReleaseList -Json $releaseJson
  $releases=$releaseList.Releases
  $existing=@($releases | Where-Object {[string]$_.tagName -eq $tag})

  if($existing.Count -gt 0){
    if($existing.Count -ne 1){throw "Multiple releases unexpectedly use tag $tag."}
    if([bool]$existing[0].isDraft){throw "Updater release $tag exists only as a draft; refusing to overwrite or publish it automatically."}
    if(-not [bool]$existing[0].isImmutable){throw "Updater release $tag exists but is not immutable; refusing to trust it as an update feed."}
    # An immutable release can be visible through GitHub's APIs before its tag is
    # advertised by Git transport. Retry verification must therefore use the same
    # authoritative REST ref as the immediate post-publication path.
    $existingRefOutput=@(& gh api "repos/$Repository/git/ref/tags/$tag")
    if($LASTEXITCODE -ne 0){throw "Existing updater tag $tag could not be inspected for verification."}
    $existingRefJson=$existingRefOutput -join [Environment]::NewLine
    $tagSha=Get-UpdaterTagCommitFromRefJson -Json $existingRefJson -ExpectedTag $tag
    if($tagSha -ne $ExpectedSourceSha){throw "Existing updater release $tag points to $tagSha instead of $ExpectedSourceSha."}

    $existingApi=& gh api "repos/$Repository/releases/tags/$tag"
    if($LASTEXITCODE -ne 0){throw "Failed to inspect existing updater release $tag."}
    $release=$existingApi | ConvertFrom-Json
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

  # Keep the release non-client-visible while assets are uploaded and verified.
  # Re-check main only after the potentially long upload window, immediately before publish.
  $publication=Invoke-UpdaterDraftPublication -ExpectedSourceSha $ExpectedSourceSha `
    -CreateDraft {
      & gh release create $tag --repo $Repository --target $ExpectedSourceSha --title "MHW Manual Mod Manager updater build $ExpectedBuildNumber" --notes $notes --draft --latest=false
      if($LASTEXITCODE -ne 0){throw "Failed to create updater draft release $tag; draft state must be inspected before retry because creation may be response-ambiguous."}
    } `
    -UploadAssets {
      # Never pass --clobber. This draft is new and exact asset names are verified below.
      & gh release upload $tag $artifact $manifestFile --repo $Repository
      if($LASTEXITCODE -ne 0){throw "Failed to upload updater draft assets for $tag."}
    } `
    -VerifyDraft {
      $draftViewJson=& gh release view $tag --repo $Repository --json tagName,isDraft,isImmutable,databaseId
      if($LASTEXITCODE -ne 0){throw "Updater draft release $tag could not be inspected."}
      $draftView=$draftViewJson | ConvertFrom-Json
      if([string]$draftView.tagName -ne $tag -or -not [bool]$draftView.isDraft){
        throw "Updater draft release $tag has unexpected identity/draft state."
      }
      $draftId=[long]$draftView.databaseId
      if($draftId -le 0){throw "Updater draft release $tag has no valid database id."}
      $draftApi=& gh api "repos/$Repository/releases/$draftId"
      if($LASTEXITCODE -ne 0){throw "Updater draft release $tag assets could not be verified."}
      $draftRelease=$draftApi | ConvertFrom-Json
      if(-not [bool]$draftRelease.draft -or [string]$draftRelease.tag_name -ne $tag){
        throw "Updater draft release $tag changed state or identity before publication."
      }
      Assert-UpdaterReleaseAssets -Release $draftRelease -Artifact $artifact -ManifestFile $manifestFile -Manifest $manifest
    } `
    -RefreshMain {
      & git fetch origin main
      if($LASTEXITCODE -ne 0){throw 'Failed to refresh origin/main after updater asset upload.'}
      return (& git rev-parse origin/main).Trim()
    } `
    -DeleteDraft {
      & gh release delete $tag --repo $Repository --cleanup-tag --yes
      if($LASTEXITCODE -ne 0){throw "Failed to withdraw updater draft release $tag."}
    } `
    -PublishDraft {
      & gh release edit $tag --repo $Repository --draft=false
      if($LASTEXITCODE -ne 0){throw "Failed to publish updater draft release $tag; publication state must be inspected before retry."}
    } `
    -EvaluateRefreshedMain {
      param([string]$refreshedMain)
      & git merge-base --is-ancestor $ExpectedSourceSha $refreshedMain
      if($LASTEXITCODE -ne 0){
        return [pscustomobject]@{Publish=$false;Reason='stale-main-non-descendant'}
      }
      $driftPaths=@(& git diff --name-only "$ExpectedSourceSha..$refreshedMain" --)
      if($LASTEXITCODE -ne 0){throw 'Failed to classify post-upload main drift for updater publication.'}
      return Get-UpdaterMainDriftDecision -CurrentSourceSha $ExpectedSourceSha -RemoteMainSha $refreshedMain -ChangedPaths $driftPaths
    }

  if(-not $publication.Published){
    Write-Host "::notice::Skipping updater publication: $($publication.Reason) (remote main $($publication.RemoteMainSha))."
    exit 0
  }

  $releaseViewJson=& gh release view $tag --repo $Repository --json tagName,isDraft,isImmutable
  if($LASTEXITCODE -ne 0){throw "Published updater release $tag could not be inspected for immutability."}
  $releaseView=$releaseViewJson | ConvertFrom-Json
  if([string]$releaseView.tagName -ne $tag -or [bool]$releaseView.isDraft){
    throw "Published updater release $tag has unexpected identity/draft state."
  }
  if(-not [bool]$releaseView.isImmutable){
    Write-Host "::error::GitHub published $tag without immutable-release protection; attempting to withdraw the invalid updater feed."
    & gh release delete $tag --repo $Repository --cleanup-tag --yes
    if($LASTEXITCODE -ne 0){Write-Host "::error::Failed to withdraw non-immutable updater release $tag."}
    throw "Updater release $tag was not immutable and is not accepted as a safe publication."
  }

  # GitHub's release API may expose the just-created tag before Git transport does.
  # Verify the published tag through the authoritative REST ref instead of treating
  # immediate fetch propagation lag as a failed release.
  $publishedRefOutput=@(& gh api "repos/$Repository/git/ref/tags/$tag")
  if($LASTEXITCODE -ne 0){throw "Published updater tag $tag could not be inspected for verification."}
  $publishedRefJson=$publishedRefOutput -join [Environment]::NewLine
  $publishedSha=Get-UpdaterTagCommitFromRefJson -Json $publishedRefJson -ExpectedTag $tag
  if($publishedSha -ne $ExpectedSourceSha){throw "Published updater tag $tag points to $publishedSha instead of $ExpectedSourceSha."}

  $publishedApi=& gh api "repos/$Repository/releases/tags/$tag"
  if($LASTEXITCODE -ne 0){throw "Published updater release $tag could not be verified."}
  $publishedRelease=$publishedApi | ConvertFrom-Json
  if([bool]$publishedRelease.draft){throw "Updater release $tag remained a draft after publication."}
  Assert-UpdaterReleaseAssets -Release $publishedRelease -Artifact $artifact -ManifestFile $manifestFile -Manifest $manifest
  Write-Host "PASS: published immutable updater release $tag for exact source $ExpectedSourceSha." -ForegroundColor Green
}
finally {
  Pop-Location
}
