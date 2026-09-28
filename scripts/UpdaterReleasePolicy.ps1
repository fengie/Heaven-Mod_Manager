Set-StrictMode -Version Latest

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
