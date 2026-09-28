Set-StrictMode -Version Latest

function ConvertFrom-UpdaterReleaseList {
  param([AllowNull()][AllowEmptyString()][string]$Json)

  if([string]::IsNullOrWhiteSpace($Json)){
    return [pscustomobject]@{Releases=[object[]]@()}
  }

  $trimmed=$Json.Trim()
  if(-not $trimmed.StartsWith('[',[StringComparison]::Ordinal) -or -not $trimmed.EndsWith(']',[StringComparison]::Ordinal)){
    throw 'GitHub release list was not a JSON array.'
  }

  try{$parsed=ConvertFrom-Json -InputObject $trimmed -ErrorAction Stop}catch{
    throw 'GitHub release list contained invalid JSON.'
  }
  if($null -eq $parsed -and $trimmed -ne '[]'){
    throw 'GitHub release list did not contain a valid release array.'
  }

  $releases=@($parsed)
  foreach($release in $releases){
    if($null -eq $release){throw 'GitHub release list contained a null entry.'}
    foreach($property in @('tagName','isDraft','isImmutable')){
      if($null -eq $release.PSObject.Properties[$property]){
        throw "GitHub release list entry omitted required property '$property'."
      }
    }
    if([string]::IsNullOrWhiteSpace([string]$release.tagName)){
      throw 'GitHub release list entry contained an empty tagName.'
    }
  }
  return [pscustomobject]@{Releases=[object[]]$releases}
}

function Get-UpdaterTagCommitFromRefJson {
  param(
    [Parameter(Mandatory=$true)][string]$Json,
    [Parameter(Mandatory=$true)][string]$ExpectedTag
  )

  if([string]::IsNullOrWhiteSpace($Json)){
    throw 'GitHub updater tag ref response was empty.'
  }

  try{$tagRef=ConvertFrom-Json -InputObject $Json -ErrorAction Stop}catch{
    throw 'GitHub updater tag ref response contained invalid JSON.'
  }
  if($null -eq $tagRef){throw 'GitHub updater tag ref response was null.'}

  $expectedRef="refs/tags/$ExpectedTag"
  if([string]$tagRef.ref -ne $expectedRef){
    throw "GitHub updater tag ref '$($tagRef.ref)' did not match expected ref '$expectedRef'."
  }
  if($null -eq $tagRef.PSObject.Properties['object'] -or $null -eq $tagRef.object){
    throw 'GitHub updater tag ref omitted its target object.'
  }
  if([string]$tagRef.object.type -ne 'commit'){
    throw "GitHub updater tag ref target type '$($tagRef.object.type)' was not a direct commit."
  }

  $sha=[string]$tagRef.object.sha
  if($sha -notmatch '^[0-9a-fA-F]{40}$'){
    throw 'GitHub updater tag ref target SHA was malformed.'
  }
  return $sha
}

function Get-UpdaterBuildFromTag {
  param([Parameter(Mandatory=$true)][string]$Tag)
  $prefix='updater-main-'
  if(-not $Tag.StartsWith($prefix,[StringComparison]::OrdinalIgnoreCase)){return -1L}
  [long]$build=0
  if(-not [long]::TryParse($Tag.Substring($prefix.Length),[ref]$build)){return -1L}
  return $build
}

function Test-UpdaterReleaseRelevantPath {
  param([Parameter(Mandatory=$true)][string]$Path)
  $normalized=$Path.Replace('\','/').TrimStart('/')
  foreach($prefix in @('src/','data/','docs/','scripts/','legacy-v7/')){
    if($normalized.StartsWith($prefix,[StringComparison]::OrdinalIgnoreCase)){return $true}
  }

  $rootFiles=@(
    'MhwModManager.sln',
    'Directory.Build.props',
    'Directory.Build.targets',
    'Directory.Packages.props',
    'NuGet.config',
    'global.json',
    'README.md',
    'CHANGELOG.md',
    'VERSION.txt',
    'VALIDATION.md',
    'Open Startup Logs.bat',
    'OPEN MASTER DEBUG LOG.bat'
  )
  return $rootFiles -icontains $normalized
}

function Get-UpdaterPublicationDecision {
  param(
    [Parameter(Mandatory=$true)][long]$CurrentBuild,
    [Parameter(Mandatory=$true)][string]$CurrentSourceSha,
    [Parameter(Mandatory=$true)][string]$RemoteMainSha,

    [long]$PreviousBuild=0,
    [string]$PreviousSourceSha='',
    [string[]]$ChangedPaths=@()
  )
  if($CurrentBuild -le 0){throw 'Current updater build number must be positive.'}
  if($CurrentSourceSha -notmatch '^[0-9a-fA-F]{7,64}$'){throw 'Current updater source SHA is malformed.'}
  if($RemoteMainSha -notmatch '^[0-9a-fA-F]{7,64}$'){throw 'Remote main SHA is malformed.'}
  if(-not [string]::Equals($CurrentSourceSha,$RemoteMainSha,[StringComparison]::OrdinalIgnoreCase)){
    return [pscustomobject]@{Publish=$false;Reason='stale-main'}
  }
  if($PreviousBuild -gt $CurrentBuild){
    throw "Updater build $CurrentBuild is older than published build $PreviousBuild."
  }
  if($PreviousBuild -eq $CurrentBuild -and $PreviousBuild -gt 0){
    return [pscustomobject]@{Publish=$false;Reason='already-published-build'}
  }
  if(-not [string]::IsNullOrWhiteSpace($PreviousSourceSha)){
    $relevant=@($ChangedPaths | Where-Object {Test-UpdaterReleaseRelevantPath $_})
    if($relevant.Count -eq 0){
      return [pscustomobject]@{Publish=$false;Reason='no-release-input-change'}
    }
  }
  return [pscustomobject]@{Publish=$true;Reason='release-input-change'}
}
