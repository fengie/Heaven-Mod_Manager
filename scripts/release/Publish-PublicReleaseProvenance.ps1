param(
  [string]$ManifestPath='',
  [string]$ArtifactPath='',
  [string]$SourceRepository=$env:GITHUB_REPOSITORY,
  [string]$PublicRepository='fengie/mhw-mod-manager-release',
  [string]$ExpectedSourceSha=$env:GITHUB_SHA,
  [ValidateRange(1,10)][int]$MaxIndexAttempts=3
)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest

$Root=(Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
. (Join-Path $PSScriptRoot 'UpdaterReleasePolicy.ps1')
. (Join-Path $PSScriptRoot 'Write-PublicReleaseProvenance.ps1')

if([string]::IsNullOrWhiteSpace($ManifestPath)){
  $ManifestPath=Join-Path $Root 'artifacts\update-manifest.json'
}
$manifestFile=(Resolve-Path -LiteralPath $ManifestPath).Path
$manifest=Get-Content -LiteralPath $manifestFile -Raw | ConvertFrom-Json
if([string]::IsNullOrWhiteSpace($ArtifactPath)){
  $ArtifactPath=Join-Path (Split-Path -Parent $manifestFile) ([string]$manifest.artifactName)
}
$artifact=(Resolve-Path -LiteralPath $ArtifactPath).Path

if($SourceRepository -ne 'fengie/mhw-mods'){throw "Unexpected provenance source repository: $SourceRepository"}
if($PublicRepository -ne 'fengie/mhw-mod-manager-release'){throw "Unexpected provenance public repository: $PublicRepository"}
if($ExpectedSourceSha -notmatch '^[0-9a-fA-F]{40}$'){throw 'Expected provenance source SHA must be a full Git SHA.'}
$expectedSource=$ExpectedSourceSha.ToLowerInvariant()
if(([string]$manifest.sourceSha).ToLowerInvariant() -ne $expectedSource){throw 'Updater manifest source does not match provenance source.'}

$sourceToken=[string]$env:GH_TOKEN
if([string]::IsNullOrWhiteSpace($sourceToken)){throw 'GH_TOKEN is required to discover the exact canonical updater release for provenance.'}
$sourceHeaders=@{
  Accept='application/vnd.github+json'
  Authorization="Bearer $sourceToken"
  'X-GitHub-Api-Version'='2022-11-28'
  'User-Agent'='mhw-mod-manager-public-provenance'
}
$releases=Get-UpdaterReleasePages -FetchPage {
  param($page,$pageSize)
  Invoke-RestMethod -UseBasicParsing -Method Get -Headers $sourceHeaders `
    -Uri "https://api.github.com/repos/$SourceRepository/releases?per_page=$pageSize&page=$page" -TimeoutSec 60
}
$exact=@($releases | Where-Object {
  $tag=[string]$_.tag_name
  $source=[string]$_.target_commitish
  $tag -match '^updater-main-[0-9]+$' -and
  -not [bool]$_.draft -and -not [bool]$_.prerelease -and [bool]$_.immutable -and
  [string]::Equals($source,$expectedSource,[StringComparison]::OrdinalIgnoreCase)
})
if($exact.Count -eq 0){
  Write-Host "::notice::No exact immutable updater release exists for source $expectedSource; public provenance reconciliation is not applicable."
  exit 0
}
if($exact.Count -ne 1){throw "Expected exactly one immutable updater release for source $expectedSource; found $($exact.Count)."}
$canonical=$exact[0]
$tag=[string]$canonical.tag_name
$build=Get-UpdaterBuildFromTag -Tag $tag
if($build -le 0){throw "Exact updater release tag '$tag' is malformed."}
$version=Get-UpdaterReleaseProductVersion -Release $canonical
if([string]::IsNullOrWhiteSpace($version)){throw "Exact updater release $tag did not expose a versioned product artifact."}

$publicToken=[string]$env:MHW_PUBLIC_RELEASE_TOKEN
if([string]::IsNullOrWhiteSpace($publicToken)){throw "MHW_PUBLIC_RELEASE_TOKEN is required to reconcile provenance for published release $tag."}
$publicHeaders=@{
  Accept='application/vnd.github+json'
  Authorization="Bearer $publicToken"
  'X-GitHub-Api-Version'='2022-11-28'
  'User-Agent'='mhw-mod-manager-public-provenance'
}
$public=Invoke-RestMethod -UseBasicParsing -Method Get -Headers $publicHeaders `
  -Uri "https://api.github.com/repos/$PublicRepository/releases/tags/$tag" -TimeoutSec 60
if([bool]$public.draft -or [bool]$public.prerelease -or -not [bool]$public.immutable){
  throw "Public updater release $tag is not immutable stable."
}
$publishedUtc=[DateTimeOffset]::Parse([string]$public.published_at,[Globalization.CultureInfo]::InvariantCulture)

$expectedNames=@([string]$manifest.artifactName,'update-manifest.json')
foreach($name in $expectedNames){
  $privateAsset=@($canonical.assets | Where-Object {[string]$_.name -eq $name})
  $publicAsset=@($public.assets | Where-Object {[string]$_.name -eq $name})
  if($privateAsset.Count -ne 1 -or $publicAsset.Count -ne 1){throw "Provenance parity requires exact asset $name once in both release repositories."}
  if([long]$privateAsset[0].size -ne [long]$publicAsset[0].size -or [string]$privateAsset[0].digest -ne [string]$publicAsset[0].digest){
    throw "Provenance parity failed for release asset $name."
  }
  if([string]$privateAsset[0].digest -notmatch '^sha256:[0-9a-fA-F]{64}$'){throw "Release asset $name omitted a SHA-256 digest."}
}

$tempRoot=''
try{
  $paths=@($artifact,$manifestFile)
  $localMatches=$true
  foreach($path in $paths){
    $name=[IO.Path]::GetFileName($path)
    $asset=@($public.assets | Where-Object {[string]$_.name -eq $name})
    if($asset.Count -ne 1){$localMatches=$false;break}
    $hash=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
    $expectedDigest=([string]$asset[0].digest).Substring('sha256:'.Length).ToLowerInvariant()
    if((Get-Item -LiteralPath $path).Length -ne [long]$asset[0].size -or $hash -ne $expectedDigest){$localMatches=$false;break}
  }

  if(-not $localMatches){
    $tempRoot=Join-Path ([IO.Path]::GetTempPath()) ('mhw-public-provenance-retry-'+[Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $tempRoot -Force | Out-Null
    $paths=@()
    foreach($name in $expectedNames){
      $asset=@($public.assets | Where-Object {[string]$_.name -eq $name})[0]
      $destination=Join-Path $tempRoot $name
      Invoke-WebRequest -UseBasicParsing -Uri ([string]$asset.browser_download_url) -OutFile $destination -TimeoutSec 300
      $hash=(Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash.ToLowerInvariant()
      $expectedDigest=([string]$asset.digest).Substring('sha256:'.Length).ToLowerInvariant()
      if((Get-Item -LiteralPath $destination).Length -ne [long]$asset.size -or $hash -ne $expectedDigest){
        throw "Downloaded immutable release asset $name did not match GitHub release metadata."
      }
      $paths += $destination
    }
  }

  $record=New-PublicReleaseProvenanceRecord `
    -Version $version `
    -Build $build `
    -SourceSha $expectedSource `
    -Tag $tag `
    -Channel 'stable' `
    -PublishedUtc $publishedUtc `
    -ArtifactPaths $paths

  $indexUri="https://api.github.com/repos/$PublicRepository/contents/release-index.json?ref=main"
  $putUri="https://api.github.com/repos/$PublicRepository/contents/release-index.json"
  for($attempt=1;$attempt -le $MaxIndexAttempts;$attempt++){
    $indexFile=Invoke-RestMethod -UseBasicParsing -Method Get -Headers $publicHeaders -Uri $indexUri -TimeoutSec 60
    if([string]$indexFile.encoding -ne 'base64' -or [string]::IsNullOrWhiteSpace([string]$indexFile.content) -or [string]$indexFile.sha -notmatch '^[0-9a-fA-F]{40}$'){
      throw 'Public release provenance index response was malformed.'
    }
    $indexBytes=[Convert]::FromBase64String(([string]$indexFile.content -replace '\s',''))
    $indexJson=[Text.Encoding]::UTF8.GetString($indexBytes)
    $decision=Add-PublicReleaseProvenanceRecordToIndexJson -IndexJson $indexJson -Record $record
    if(-not $decision.Added){
      Write-Host "PASS: public provenance index already contains exact immutable release $tag." -ForegroundColor Green
      exit 0
    }

    $content=[Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes([string]$decision.Json))
    $body=@{
      message="Append updater provenance $tag"
      content=$content
      sha=[string]$indexFile.sha
      branch='main'
    } | ConvertTo-Json -Depth 4
    try{
      [void](Invoke-RestMethod -UseBasicParsing -Method Put -Headers $publicHeaders -Uri $putUri -Body $body -ContentType 'application/json' -TimeoutSec 60)
    }catch{
      if($attempt -ge $MaxIndexAttempts){throw}
      Write-Host "::notice::Public provenance index CAS attempt $attempt raced another writer; refreshing before retry."
      Start-Sleep -Seconds 1
      continue
    }

    $verifyFile=Invoke-RestMethod -UseBasicParsing -Method Get -Headers $publicHeaders -Uri $indexUri -TimeoutSec 60
    $verifyJson=[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String(([string]$verifyFile.content -replace '\s','')))
    $verifyDecision=Add-PublicReleaseProvenanceRecordToIndexJson -IndexJson $verifyJson -Record $record
    if($verifyDecision.Added){throw "Public provenance index write for $tag returned success but the exact record is still absent."}
    Write-Host "PASS: appended exact immutable updater provenance $tag / $expectedSource to $PublicRepository." -ForegroundColor Green
    exit 0
  }
  throw "Failed to reconcile public release provenance index for $tag."
}
finally{
  if(-not [string]::IsNullOrWhiteSpace($tempRoot) -and (Test-Path -LiteralPath $tempRoot)){
    Remove-Item -LiteralPath $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
  }
}
