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
  if([string]::Equals($normalized,'.github/workflows/windows-release-gate.yml',[StringComparison]::OrdinalIgnoreCase)){return $true}
  foreach($prefix in @('src/','tests/','data/','docs/','scripts/','legacy-v7/')){
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

function Get-UpdaterMainDriftDecision {
  param(
    [Parameter(Mandatory=$true)][string]$CurrentSourceSha,
    [Parameter(Mandatory=$true)][string]$RemoteMainSha,
    [string[]]$ChangedPaths=@()
  )
  if($CurrentSourceSha -notmatch '^[0-9a-fA-F]{7,64}$'){throw 'Current updater source SHA is malformed.'}
  if($RemoteMainSha -notmatch '^[0-9a-fA-F]{7,64}$'){throw 'Remote main SHA is malformed.'}
  if([string]::Equals($CurrentSourceSha,$RemoteMainSha,[StringComparison]::OrdinalIgnoreCase)){
    return [pscustomobject]@{Publish=$true;Reason='exact-main';RelevantPaths=[string[]]@()}
  }
  if($ChangedPaths.Count -eq 0){
    return [pscustomobject]@{Publish=$false;Reason='stale-main-unclassified';RelevantPaths=[string[]]@()}
  }
  $relevant=@($ChangedPaths | Where-Object {Test-UpdaterReleaseRelevantPath $_})
  if($relevant.Count -gt 0){
    return [pscustomobject]@{Publish=$false;Reason='stale-main-release-input-change';RelevantPaths=[string[]]$relevant}
  }
  return [pscustomobject]@{Publish=$true;Reason='release-inputs-unchanged';RelevantPaths=[string[]]@()}
}


function Get-UpdaterReleaseProductVersion {
  param([Parameter(Mandatory=$true)]$Release)

  $tag=[string]$Release.tag_name
  if((Get-UpdaterBuildFromTag -Tag $tag) -le 0){return ''}

  foreach($property in @('draft','prerelease','immutable','assets')){
    if($null -eq $Release.PSObject.Properties[$property]){
      throw "Updater release $tag omitted required API property '$property'."
    }
  }
  if([bool]$Release.draft -or [bool]$Release.prerelease -or -not [bool]$Release.immutable){return ''}

  $pattern='^MHW-Manual-Mod-Manager-v([0-9]+\.[0-9]+\.[0-9]+)-win-x64\.zip
  param(
    [Parameter(Mandatory=$true)][string]$SourceSha,
    [Parameter(Mandatory=$true)][string]$RemoteMainSha,
    [Parameter(Mandatory=$true)][bool]$ExactReleaseFound,
    [Parameter(Mandatory=$true)][string]$MainRelation
  )

  foreach($value in @($SourceSha,$RemoteMainSha)){
    if($value -notmatch '^[0-9a-fA-F]{40}$'){
      throw 'Updater installed-client E2E source/main SHA was malformed.'
    }
  }

  if($ExactReleaseFound){
    return [pscustomobject]@{RunE2E=$true;Reason='exact-release-published'}
  }

  if([string]::Equals($SourceSha,$RemoteMainSha,[StringComparison]::OrdinalIgnoreCase)){
    throw "Successful Windows Release Gate for canonical source $SourceSha did not publish an exact immutable updater release."
  }

  if([string]::Equals($MainRelation,'ahead',[StringComparison]::OrdinalIgnoreCase)){
    return [pscustomobject]@{RunE2E=$false;Reason='superseded-before-publication'}
  }

  throw "Successful Windows Release Gate source $SourceSha has no exact immutable updater release and cannot be classified as a canonical-main supersession (main=$RemoteMainSha relation=$MainRelation)."
}


function ConvertTo-UpdaterInstalledClientDurableEvidenceJson {
  param(
    [Parameter(Mandatory=$true)][string]$Json,
    [Parameter(Mandatory=$true)][string]$ExpectedTargetSourceSha
  )

  if([string]::IsNullOrWhiteSpace($Json)){ throw 'Updater installed-client E2E evidence was empty.' }
  try{$doc=ConvertFrom-Json -InputObject $Json -ErrorAction Stop}catch{
    throw 'Updater installed-client E2E evidence contained invalid JSON.'
  }
  if($null -eq $doc){ throw 'Updater installed-client E2E evidence was null.' }

  $requiredProperty={
    param($Object,[string]$Name)
    if($null -eq $Object){ throw "Updater installed-client E2E evidence omitted required object for '$Name'." }
    $property=$Object.PSObject.Properties[$Name]
    if($null -eq $property){ throw "Updater installed-client E2E evidence omitted required property '$Name'." }
    return $property.Value
  }
  $requiredSha={
    param($Object,[string]$Name)
    $value=[string](& $requiredProperty $Object $Name)
    if($value -notmatch '^[0-9a-fA-F]{40}$'){ throw "Updater installed-client E2E evidence property '$Name' was not a 40-hex source SHA." }
    return $value.ToLowerInvariant()
  }
  $requiredHash={
    param($Object,[string]$Name)
    $value=[string](& $requiredProperty $Object $Name)
    if($value -notmatch '^[0-9a-fA-F]{64}$'){ throw "Updater installed-client E2E sentinel '$Name' was not a SHA-256 digest." }
    return $value.ToUpperInvariant()
  }

  $schemaVersion=[int](& $requiredProperty $doc 'schemaVersion')
  if($schemaVersion -ne 1){ throw "Unsupported updater installed-client E2E evidence schema version '$schemaVersion'." }
  if($ExpectedTargetSourceSha -notmatch '^[0-9a-fA-F]{40}$'){
    throw 'Expected updater installed-client E2E target source was not a 40-hex SHA.'
  }
  $expectedTargetSource=$ExpectedTargetSourceSha.ToLowerInvariant()

  $oldTag=[string](& $requiredProperty $doc 'oldTag')
  $oldBuild=[long](& $requiredProperty $doc 'oldBuild')
  $oldSource=& $requiredSha $doc 'oldSource'
  $targetBuild=[long](& $requiredProperty $doc 'targetBuild')
  $targetSource=& $requiredSha $doc 'targetSource'
  if($oldTag -notmatch '^updater-main-[0-9]{1,18}$' -or (Get-UpdaterBuildFromTag -Tag $oldTag) -ne $oldBuild -or $oldBuild -lt 0 -or $targetBuild -le $oldBuild){
    throw 'Updater installed-client E2E evidence contained an invalid old/target release identity.'
  }
  if(-not [string]::Equals($targetSource,$expectedTargetSource,[StringComparison]::OrdinalIgnoreCase)){
    throw "Updater installed-client E2E target source '$targetSource' did not match exact tested source '$expectedTargetSource'."
  }

  $scenarioA=& $requiredProperty $doc 'scenarioA'
  $scenarioB=& $requiredProperty $doc 'scenarioB'
  if([string](& $requiredProperty $scenarioA 'status') -ne 'PASS'){ throw 'Updater installed-client E2E update scenario was not PASS.' }
  if([string](& $requiredProperty $scenarioB 'status') -ne 'PASS'){ throw 'Updater installed-client E2E rollback scenario was not PASS.' }

  $scenarioATargetBuild=[long](& $requiredProperty $scenarioA 'targetBuild')
  $scenarioATargetSource=& $requiredSha $scenarioA 'targetSource'
  $healthBuild=[long](& $requiredProperty $scenarioA 'healthBuild')
  $healthSource=& $requiredSha $scenarioA 'healthSource'
  $oldClientExitCode=[int](& $requiredProperty $scenarioA 'oldClientExitCode')
  $selectorDisplayText=[string](& $requiredProperty $scenarioA 'selectorDisplayText')
  $switchButtonEnabled=[bool](& $requiredProperty $scenarioA 'switchButtonEnabled')
  $settingsButtonEnabled=[bool](& $requiredProperty $scenarioA 'settingsButtonEnabled')
  $scenarioAJournalPhase=[string](& $requiredProperty $scenarioA 'journalPhase')
  if($scenarioATargetBuild -ne $targetBuild -or
     -not [string]::Equals($scenarioATargetSource,$targetSource,[StringComparison]::OrdinalIgnoreCase) -or
     $healthBuild -ne $targetBuild -or
     -not [string]::Equals($healthSource,$targetSource,[StringComparison]::OrdinalIgnoreCase) -or
     $oldClientExitCode -ne 0 -or
     $selectorDisplayText -ne 'Updater E2E Fake Game' -or
     -not $switchButtonEnabled -or
     -not $settingsButtonEnabled -or
     $scenarioAJournalPhase -ne 'Confirmed'){
    throw 'Updater installed-client E2E update evidence failed durable-attestation validation.'
  }

  $restoredBuild=[long](& $requiredProperty $scenarioB 'restoredBuild')
  $restoredSource=& $requiredSha $scenarioB 'restoredSource'
  $oldOwnedFileCount=[long](& $requiredProperty $scenarioB 'oldOwnedFileCount')
  $targetOnlyFileCount=[long](& $requiredProperty $scenarioB 'targetOnlyFileCount')
  $scenarioBJournalPhase=[string](& $requiredProperty $scenarioB 'journalPhase')
  if($restoredBuild -ne $oldBuild -or
     -not [string]::Equals($restoredSource,$oldSource,[StringComparison]::OrdinalIgnoreCase) -or
     $oldOwnedFileCount -lt 0 -or
     $targetOnlyFileCount -lt 0 -or
     $scenarioBJournalPhase -ne 'RolledBack'){
    throw 'Updater installed-client E2E rollback evidence failed durable-attestation validation.'
  }

  $scenarioASentinels=& $requiredProperty $scenarioA 'sentinelSha256'
  $scenarioBSentinels=& $requiredProperty $scenarioB 'sentinelSha256'
  $sentinelNames=[ordered]@{
    mod='Mods/e2e-user.mod'
    state='State/e2e-state.json'
    unknownUserFile='e2e-unknown-user-file.txt'
  }
  $scenarioAHashes=[ordered]@{}
  $scenarioBHashes=[ordered]@{}
  foreach($entry in $sentinelNames.GetEnumerator()){
    $scenarioAHashes[$entry.Key]=& $requiredHash $scenarioASentinels $entry.Value
    $scenarioBHashes[$entry.Key]=& $requiredHash $scenarioBSentinels $entry.Value
  }

  $durable=[ordered]@{
    schema='mhw-mod-manager/updater-installed-client-e2e-durable/v1'
    sourceEvidenceSchemaVersion=$schemaVersion
    oldRelease=[ordered]@{tag=$oldTag;build=$oldBuild;source=$oldSource}
    target=[ordered]@{build=$targetBuild;source=$targetSource}
    update=[ordered]@{
      status='PASS'
      targetBuild=$scenarioATargetBuild
      targetSource=$scenarioATargetSource
      oldClientExitCode=$oldClientExitCode
      healthBuild=$healthBuild
      healthSource=$healthSource
      selectorDisplayText='Updater E2E Fake Game'
      switchButtonEnabled=$true
      settingsButtonEnabled=$true
      journalPhase='Confirmed'
      sentinelSha256=$scenarioAHashes
    }
    rollback=[ordered]@{
      status='PASS'
      journalPhase='RolledBack'
      restoredBuild=$restoredBuild
      restoredSource=$restoredSource
      oldOwnedFileCount=$oldOwnedFileCount
      targetOnlyFileCount=$targetOnlyFileCount
      sentinelSha256=$scenarioBHashes
    }
  }
  return ($durable | ConvertTo-Json -Depth 8)
}

function Get-UpdaterPublicationDecision {
  param(
    [Parameter(Mandatory=$true)][long]$CurrentBuild,
    [Parameter(Mandatory=$true)][string]$CurrentSourceSha,
    [Parameter(Mandatory=$true)][string]$RemoteMainSha,

    [long]$PreviousBuild=0,
    [string]$PreviousSourceSha='',
    [string[]]$ChangedPaths=@(),
    [string[]]$RemoteMainChangedPaths=@()
  )
  if($CurrentBuild -le 0){throw 'Current updater build number must be positive.'}
  $drift=Get-UpdaterMainDriftDecision -CurrentSourceSha $CurrentSourceSha -RemoteMainSha $RemoteMainSha -ChangedPaths $RemoteMainChangedPaths
  if(-not $drift.Publish){
    return [pscustomobject]@{Publish=$false;Reason=$drift.Reason}
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

  $artifactNames=@($Release.assets | ForEach-Object {[string]$_.name})
  $matches=@($artifactNames | Where-Object {$_ -match $pattern})
  if($matches.Count -ne 1){
    throw "Stable immutable updater release $tag does not expose exactly one versioned Windows artifact."
  }
  $match=[regex]::Match([string]$matches[0],$pattern)
  if(-not $match.Success){throw "Updater release $tag versioned artifact could not be parsed."}
  return [string]$match.Groups[1].Value
}

function Get-UpdaterReleaseIntentDecision {
  param(
    [Parameter(Mandatory=$true)][string]$CurrentVersion,
    [Parameter(Mandatory=$true)][bool]$ForcePublish,
    [object[]]$Releases=@()
  )

  if($CurrentVersion -notmatch '^[0-9]+\.[0-9]+\.[0-9]+
  param(
    [Parameter(Mandatory=$true)][string]$SourceSha,
    [Parameter(Mandatory=$true)][string]$RemoteMainSha,
    [Parameter(Mandatory=$true)][bool]$ExactReleaseFound,
    [Parameter(Mandatory=$true)][string]$MainRelation
  )

  foreach($value in @($SourceSha,$RemoteMainSha)){
    if($value -notmatch '^[0-9a-fA-F]{40}$'){
      throw 'Updater installed-client E2E source/main SHA was malformed.'
    }
  }

  if($ExactReleaseFound){
    return [pscustomobject]@{RunE2E=$true;Reason='exact-release-published'}
  }

  if([string]::Equals($SourceSha,$RemoteMainSha,[StringComparison]::OrdinalIgnoreCase)){
    throw "Successful Windows Release Gate for canonical source $SourceSha did not publish an exact immutable updater release."
  }

  if([string]::Equals($MainRelation,'ahead',[StringComparison]::OrdinalIgnoreCase)){
    return [pscustomobject]@{RunE2E=$false;Reason='superseded-before-publication'}
  }

  throw "Successful Windows Release Gate source $SourceSha has no exact immutable updater release and cannot be classified as a canonical-main supersession (main=$RemoteMainSha relation=$MainRelation)."
}


function ConvertTo-UpdaterInstalledClientDurableEvidenceJson {
  param(
    [Parameter(Mandatory=$true)][string]$Json,
    [Parameter(Mandatory=$true)][string]$ExpectedTargetSourceSha
  )

  if([string]::IsNullOrWhiteSpace($Json)){ throw 'Updater installed-client E2E evidence was empty.' }
  try{$doc=ConvertFrom-Json -InputObject $Json -ErrorAction Stop}catch{
    throw 'Updater installed-client E2E evidence contained invalid JSON.'
  }
  if($null -eq $doc){ throw 'Updater installed-client E2E evidence was null.' }

  $requiredProperty={
    param($Object,[string]$Name)
    if($null -eq $Object){ throw "Updater installed-client E2E evidence omitted required object for '$Name'." }
    $property=$Object.PSObject.Properties[$Name]
    if($null -eq $property){ throw "Updater installed-client E2E evidence omitted required property '$Name'." }
    return $property.Value
  }
  $requiredSha={
    param($Object,[string]$Name)
    $value=[string](& $requiredProperty $Object $Name)
    if($value -notmatch '^[0-9a-fA-F]{40}$'){ throw "Updater installed-client E2E evidence property '$Name' was not a 40-hex source SHA." }
    return $value.ToLowerInvariant()
  }
  $requiredHash={
    param($Object,[string]$Name)
    $value=[string](& $requiredProperty $Object $Name)
    if($value -notmatch '^[0-9a-fA-F]{64}$'){ throw "Updater installed-client E2E sentinel '$Name' was not a SHA-256 digest." }
    return $value.ToUpperInvariant()
  }

  $schemaVersion=[int](& $requiredProperty $doc 'schemaVersion')
  if($schemaVersion -ne 1){ throw "Unsupported updater installed-client E2E evidence schema version '$schemaVersion'." }
  if($ExpectedTargetSourceSha -notmatch '^[0-9a-fA-F]{40}$'){
    throw 'Expected updater installed-client E2E target source was not a 40-hex SHA.'
  }
  $expectedTargetSource=$ExpectedTargetSourceSha.ToLowerInvariant()

  $oldTag=[string](& $requiredProperty $doc 'oldTag')
  $oldBuild=[long](& $requiredProperty $doc 'oldBuild')
  $oldSource=& $requiredSha $doc 'oldSource'
  $targetBuild=[long](& $requiredProperty $doc 'targetBuild')
  $targetSource=& $requiredSha $doc 'targetSource'
  if($oldTag -notmatch '^updater-main-[0-9]{1,18}$' -or (Get-UpdaterBuildFromTag -Tag $oldTag) -ne $oldBuild -or $oldBuild -lt 0 -or $targetBuild -le $oldBuild){
    throw 'Updater installed-client E2E evidence contained an invalid old/target release identity.'
  }
  if(-not [string]::Equals($targetSource,$expectedTargetSource,[StringComparison]::OrdinalIgnoreCase)){
    throw "Updater installed-client E2E target source '$targetSource' did not match exact tested source '$expectedTargetSource'."
  }

  $scenarioA=& $requiredProperty $doc 'scenarioA'
  $scenarioB=& $requiredProperty $doc 'scenarioB'
  if([string](& $requiredProperty $scenarioA 'status') -ne 'PASS'){ throw 'Updater installed-client E2E update scenario was not PASS.' }
  if([string](& $requiredProperty $scenarioB 'status') -ne 'PASS'){ throw 'Updater installed-client E2E rollback scenario was not PASS.' }

  $scenarioATargetBuild=[long](& $requiredProperty $scenarioA 'targetBuild')
  $scenarioATargetSource=& $requiredSha $scenarioA 'targetSource'
  $healthBuild=[long](& $requiredProperty $scenarioA 'healthBuild')
  $healthSource=& $requiredSha $scenarioA 'healthSource'
  $oldClientExitCode=[int](& $requiredProperty $scenarioA 'oldClientExitCode')
  $selectorDisplayText=[string](& $requiredProperty $scenarioA 'selectorDisplayText')
  $switchButtonEnabled=[bool](& $requiredProperty $scenarioA 'switchButtonEnabled')
  $settingsButtonEnabled=[bool](& $requiredProperty $scenarioA 'settingsButtonEnabled')
  $scenarioAJournalPhase=[string](& $requiredProperty $scenarioA 'journalPhase')
  if($scenarioATargetBuild -ne $targetBuild -or
     -not [string]::Equals($scenarioATargetSource,$targetSource,[StringComparison]::OrdinalIgnoreCase) -or
     $healthBuild -ne $targetBuild -or
     -not [string]::Equals($healthSource,$targetSource,[StringComparison]::OrdinalIgnoreCase) -or
     $oldClientExitCode -ne 0 -or
     $selectorDisplayText -ne 'Updater E2E Fake Game' -or
     -not $switchButtonEnabled -or
     -not $settingsButtonEnabled -or
     $scenarioAJournalPhase -ne 'Confirmed'){
    throw 'Updater installed-client E2E update evidence failed durable-attestation validation.'
  }

  $restoredBuild=[long](& $requiredProperty $scenarioB 'restoredBuild')
  $restoredSource=& $requiredSha $scenarioB 'restoredSource'
  $oldOwnedFileCount=[long](& $requiredProperty $scenarioB 'oldOwnedFileCount')
  $targetOnlyFileCount=[long](& $requiredProperty $scenarioB 'targetOnlyFileCount')
  $scenarioBJournalPhase=[string](& $requiredProperty $scenarioB 'journalPhase')
  if($restoredBuild -ne $oldBuild -or
     -not [string]::Equals($restoredSource,$oldSource,[StringComparison]::OrdinalIgnoreCase) -or
     $oldOwnedFileCount -lt 0 -or
     $targetOnlyFileCount -lt 0 -or
     $scenarioBJournalPhase -ne 'RolledBack'){
    throw 'Updater installed-client E2E rollback evidence failed durable-attestation validation.'
  }

  $scenarioASentinels=& $requiredProperty $scenarioA 'sentinelSha256'
  $scenarioBSentinels=& $requiredProperty $scenarioB 'sentinelSha256'
  $sentinelNames=[ordered]@{
    mod='Mods/e2e-user.mod'
    state='State/e2e-state.json'
    unknownUserFile='e2e-unknown-user-file.txt'
  }
  $scenarioAHashes=[ordered]@{}
  $scenarioBHashes=[ordered]@{}
  foreach($entry in $sentinelNames.GetEnumerator()){
    $scenarioAHashes[$entry.Key]=& $requiredHash $scenarioASentinels $entry.Value
    $scenarioBHashes[$entry.Key]=& $requiredHash $scenarioBSentinels $entry.Value
  }

  $durable=[ordered]@{
    schema='mhw-mod-manager/updater-installed-client-e2e-durable/v1'
    sourceEvidenceSchemaVersion=$schemaVersion
    oldRelease=[ordered]@{tag=$oldTag;build=$oldBuild;source=$oldSource}
    target=[ordered]@{build=$targetBuild;source=$targetSource}
    update=[ordered]@{
      status='PASS'
      targetBuild=$scenarioATargetBuild
      targetSource=$scenarioATargetSource
      oldClientExitCode=$oldClientExitCode
      healthBuild=$healthBuild
      healthSource=$healthSource
      selectorDisplayText='Updater E2E Fake Game'
      switchButtonEnabled=$true
      settingsButtonEnabled=$true
      journalPhase='Confirmed'
      sentinelSha256=$scenarioAHashes
    }
    rollback=[ordered]@{
      status='PASS'
      journalPhase='RolledBack'
      restoredBuild=$restoredBuild
      restoredSource=$restoredSource
      oldOwnedFileCount=$oldOwnedFileCount
      targetOnlyFileCount=$targetOnlyFileCount
      sentinelSha256=$scenarioBHashes
    }
  }
  return ($durable | ConvertTo-Json -Depth 8)
}

function Get-UpdaterPublicationDecision {
  param(
    [Parameter(Mandatory=$true)][long]$CurrentBuild,
    [Parameter(Mandatory=$true)][string]$CurrentSourceSha,
    [Parameter(Mandatory=$true)][string]$RemoteMainSha,

    [long]$PreviousBuild=0,
    [string]$PreviousSourceSha='',
    [string[]]$ChangedPaths=@(),
    [string[]]$RemoteMainChangedPaths=@()
  )
  if($CurrentBuild -le 0){throw 'Current updater build number must be positive.'}
  $drift=Get-UpdaterMainDriftDecision -CurrentSourceSha $CurrentSourceSha -RemoteMainSha $RemoteMainSha -ChangedPaths $RemoteMainChangedPaths
  if(-not $drift.Publish){
    return [pscustomobject]@{Publish=$false;Reason=$drift.Reason}
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
){
    throw "Updater product version '$CurrentVersion' is malformed."
  }
  if($ForcePublish){
    return [pscustomobject]@{Publish=$true;Reason='manual-force-publish';ExistingTag=''}
  }

  $sameVersion=New-Object System.Collections.Generic.List[object]
  foreach($release in @($Releases)){
    if($null -eq $release){throw 'Updater release intent received a null release entry.'}
    $version=Get-UpdaterReleaseProductVersion -Release $release
    if([string]::IsNullOrWhiteSpace($version)){continue}
    if([string]::Equals($version,$CurrentVersion,[StringComparison]::Ordinal)){
      $sameVersion.Add([pscustomobject]@{
        Build=(Get-UpdaterBuildFromTag -Tag ([string]$release.tag_name))
        Tag=[string]$release.tag_name
      })
    }
  }

  if($sameVersion.Count -gt 0){
    $existing=@($sameVersion | Sort-Object Build -Descending | Select-Object -First 1)
    return [pscustomobject]@{Publish=$false;Reason='same-version-already-published';ExistingTag=[string]$existing[0].Tag}
  }

  return [pscustomobject]@{Publish=$true;Reason='new-product-version';ExistingTag=''}
}


function Get-UpdaterInstalledClientE2EDecision {
  param(
    [Parameter(Mandatory=$true)][string]$SourceSha,
    [Parameter(Mandatory=$true)][string]$RemoteMainSha,
    [Parameter(Mandatory=$true)][bool]$ExactReleaseFound,
    [Parameter(Mandatory=$true)][bool]$SameVersionReleaseFound,
    [Parameter(Mandatory=$true)][string]$MainRelation
  )

  foreach($value in @($SourceSha,$RemoteMainSha)){
    if($value -notmatch '^[0-9a-fA-F]{40}


function ConvertTo-UpdaterInstalledClientDurableEvidenceJson {
  param(
    [Parameter(Mandatory=$true)][string]$Json,
    [Parameter(Mandatory=$true)][string]$ExpectedTargetSourceSha
  )

  if([string]::IsNullOrWhiteSpace($Json)){ throw 'Updater installed-client E2E evidence was empty.' }
  try{$doc=ConvertFrom-Json -InputObject $Json -ErrorAction Stop}catch{
    throw 'Updater installed-client E2E evidence contained invalid JSON.'
  }
  if($null -eq $doc){ throw 'Updater installed-client E2E evidence was null.' }

  $requiredProperty={
    param($Object,[string]$Name)
    if($null -eq $Object){ throw "Updater installed-client E2E evidence omitted required object for '$Name'." }
    $property=$Object.PSObject.Properties[$Name]
    if($null -eq $property){ throw "Updater installed-client E2E evidence omitted required property '$Name'." }
    return $property.Value
  }
  $requiredSha={
    param($Object,[string]$Name)
    $value=[string](& $requiredProperty $Object $Name)
    if($value -notmatch '^[0-9a-fA-F]{40}$'){ throw "Updater installed-client E2E evidence property '$Name' was not a 40-hex source SHA." }
    return $value.ToLowerInvariant()
  }
  $requiredHash={
    param($Object,[string]$Name)
    $value=[string](& $requiredProperty $Object $Name)
    if($value -notmatch '^[0-9a-fA-F]{64}$'){ throw "Updater installed-client E2E sentinel '$Name' was not a SHA-256 digest." }
    return $value.ToUpperInvariant()
  }

  $schemaVersion=[int](& $requiredProperty $doc 'schemaVersion')
  if($schemaVersion -ne 1){ throw "Unsupported updater installed-client E2E evidence schema version '$schemaVersion'." }
  if($ExpectedTargetSourceSha -notmatch '^[0-9a-fA-F]{40}$'){
    throw 'Expected updater installed-client E2E target source was not a 40-hex SHA.'
  }
  $expectedTargetSource=$ExpectedTargetSourceSha.ToLowerInvariant()

  $oldTag=[string](& $requiredProperty $doc 'oldTag')
  $oldBuild=[long](& $requiredProperty $doc 'oldBuild')
  $oldSource=& $requiredSha $doc 'oldSource'
  $targetBuild=[long](& $requiredProperty $doc 'targetBuild')
  $targetSource=& $requiredSha $doc 'targetSource'
  if($oldTag -notmatch '^updater-main-[0-9]{1,18}$' -or (Get-UpdaterBuildFromTag -Tag $oldTag) -ne $oldBuild -or $oldBuild -lt 0 -or $targetBuild -le $oldBuild){
    throw 'Updater installed-client E2E evidence contained an invalid old/target release identity.'
  }
  if(-not [string]::Equals($targetSource,$expectedTargetSource,[StringComparison]::OrdinalIgnoreCase)){
    throw "Updater installed-client E2E target source '$targetSource' did not match exact tested source '$expectedTargetSource'."
  }

  $scenarioA=& $requiredProperty $doc 'scenarioA'
  $scenarioB=& $requiredProperty $doc 'scenarioB'
  if([string](& $requiredProperty $scenarioA 'status') -ne 'PASS'){ throw 'Updater installed-client E2E update scenario was not PASS.' }
  if([string](& $requiredProperty $scenarioB 'status') -ne 'PASS'){ throw 'Updater installed-client E2E rollback scenario was not PASS.' }

  $scenarioATargetBuild=[long](& $requiredProperty $scenarioA 'targetBuild')
  $scenarioATargetSource=& $requiredSha $scenarioA 'targetSource'
  $healthBuild=[long](& $requiredProperty $scenarioA 'healthBuild')
  $healthSource=& $requiredSha $scenarioA 'healthSource'
  $oldClientExitCode=[int](& $requiredProperty $scenarioA 'oldClientExitCode')
  $selectorDisplayText=[string](& $requiredProperty $scenarioA 'selectorDisplayText')
  $switchButtonEnabled=[bool](& $requiredProperty $scenarioA 'switchButtonEnabled')
  $settingsButtonEnabled=[bool](& $requiredProperty $scenarioA 'settingsButtonEnabled')
  $scenarioAJournalPhase=[string](& $requiredProperty $scenarioA 'journalPhase')
  if($scenarioATargetBuild -ne $targetBuild -or
     -not [string]::Equals($scenarioATargetSource,$targetSource,[StringComparison]::OrdinalIgnoreCase) -or
     $healthBuild -ne $targetBuild -or
     -not [string]::Equals($healthSource,$targetSource,[StringComparison]::OrdinalIgnoreCase) -or
     $oldClientExitCode -ne 0 -or
     $selectorDisplayText -ne 'Updater E2E Fake Game' -or
     -not $switchButtonEnabled -or
     -not $settingsButtonEnabled -or
     $scenarioAJournalPhase -ne 'Confirmed'){
    throw 'Updater installed-client E2E update evidence failed durable-attestation validation.'
  }

  $restoredBuild=[long](& $requiredProperty $scenarioB 'restoredBuild')
  $restoredSource=& $requiredSha $scenarioB 'restoredSource'
  $oldOwnedFileCount=[long](& $requiredProperty $scenarioB 'oldOwnedFileCount')
  $targetOnlyFileCount=[long](& $requiredProperty $scenarioB 'targetOnlyFileCount')
  $scenarioBJournalPhase=[string](& $requiredProperty $scenarioB 'journalPhase')
  if($restoredBuild -ne $oldBuild -or
     -not [string]::Equals($restoredSource,$oldSource,[StringComparison]::OrdinalIgnoreCase) -or
     $oldOwnedFileCount -lt 0 -or
     $targetOnlyFileCount -lt 0 -or
     $scenarioBJournalPhase -ne 'RolledBack'){
    throw 'Updater installed-client E2E rollback evidence failed durable-attestation validation.'
  }

  $scenarioASentinels=& $requiredProperty $scenarioA 'sentinelSha256'
  $scenarioBSentinels=& $requiredProperty $scenarioB 'sentinelSha256'
  $sentinelNames=[ordered]@{
    mod='Mods/e2e-user.mod'
    state='State/e2e-state.json'
    unknownUserFile='e2e-unknown-user-file.txt'
  }
  $scenarioAHashes=[ordered]@{}
  $scenarioBHashes=[ordered]@{}
  foreach($entry in $sentinelNames.GetEnumerator()){
    $scenarioAHashes[$entry.Key]=& $requiredHash $scenarioASentinels $entry.Value
    $scenarioBHashes[$entry.Key]=& $requiredHash $scenarioBSentinels $entry.Value
  }

  $durable=[ordered]@{
    schema='mhw-mod-manager/updater-installed-client-e2e-durable/v1'
    sourceEvidenceSchemaVersion=$schemaVersion
    oldRelease=[ordered]@{tag=$oldTag;build=$oldBuild;source=$oldSource}
    target=[ordered]@{build=$targetBuild;source=$targetSource}
    update=[ordered]@{
      status='PASS'
      targetBuild=$scenarioATargetBuild
      targetSource=$scenarioATargetSource
      oldClientExitCode=$oldClientExitCode
      healthBuild=$healthBuild
      healthSource=$healthSource
      selectorDisplayText='Updater E2E Fake Game'
      switchButtonEnabled=$true
      settingsButtonEnabled=$true
      journalPhase='Confirmed'
      sentinelSha256=$scenarioAHashes
    }
    rollback=[ordered]@{
      status='PASS'
      journalPhase='RolledBack'
      restoredBuild=$restoredBuild
      restoredSource=$restoredSource
      oldOwnedFileCount=$oldOwnedFileCount
      targetOnlyFileCount=$targetOnlyFileCount
      sentinelSha256=$scenarioBHashes
    }
  }
  return ($durable | ConvertTo-Json -Depth 8)
}

function Get-UpdaterPublicationDecision {
  param(
    [Parameter(Mandatory=$true)][long]$CurrentBuild,
    [Parameter(Mandatory=$true)][string]$CurrentSourceSha,
    [Parameter(Mandatory=$true)][string]$RemoteMainSha,

    [long]$PreviousBuild=0,
    [string]$PreviousSourceSha='',
    [string[]]$ChangedPaths=@(),
    [string[]]$RemoteMainChangedPaths=@()
  )
  if($CurrentBuild -le 0){throw 'Current updater build number must be positive.'}
  $drift=Get-UpdaterMainDriftDecision -CurrentSourceSha $CurrentSourceSha -RemoteMainSha $RemoteMainSha -ChangedPaths $RemoteMainChangedPaths
  if(-not $drift.Publish){
    return [pscustomobject]@{Publish=$false;Reason=$drift.Reason}
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
){
      throw 'Updater installed-client E2E source/main SHA was malformed.'
    }
  }

  if($ExactReleaseFound){
    return [pscustomobject]@{RunE2E=$true;Reason='exact-release-published'}
  }

  $canonicalOrDescendant=[string]::Equals($SourceSha,$RemoteMainSha,[StringComparison]::OrdinalIgnoreCase) -or
    [string]::Equals($MainRelation,'ahead',[StringComparison]::OrdinalIgnoreCase)
  if($SameVersionReleaseFound -and $canonicalOrDescendant){
    return [pscustomobject]@{RunE2E=$false;Reason='same-version-already-published'}
  }

  if([string]::Equals($SourceSha,$RemoteMainSha,[StringComparison]::OrdinalIgnoreCase)){
    throw "Successful Windows Release Gate for canonical source $SourceSha did not publish an exact immutable updater release."
  }

  if([string]::Equals($MainRelation,'ahead',[StringComparison]::OrdinalIgnoreCase)){
    return [pscustomobject]@{RunE2E=$false;Reason='superseded-before-publication'}
  }

  throw "Successful Windows Release Gate source $SourceSha has no exact immutable updater release and cannot be classified as a canonical-main supersession (main=$RemoteMainSha relation=$MainRelation)."
}


function ConvertTo-UpdaterInstalledClientDurableEvidenceJson {
  param(
    [Parameter(Mandatory=$true)][string]$Json,
    [Parameter(Mandatory=$true)][string]$ExpectedTargetSourceSha
  )

  if([string]::IsNullOrWhiteSpace($Json)){ throw 'Updater installed-client E2E evidence was empty.' }
  try{$doc=ConvertFrom-Json -InputObject $Json -ErrorAction Stop}catch{
    throw 'Updater installed-client E2E evidence contained invalid JSON.'
  }
  if($null -eq $doc){ throw 'Updater installed-client E2E evidence was null.' }

  $requiredProperty={
    param($Object,[string]$Name)
    if($null -eq $Object){ throw "Updater installed-client E2E evidence omitted required object for '$Name'." }
    $property=$Object.PSObject.Properties[$Name]
    if($null -eq $property){ throw "Updater installed-client E2E evidence omitted required property '$Name'." }
    return $property.Value
  }
  $requiredSha={
    param($Object,[string]$Name)
    $value=[string](& $requiredProperty $Object $Name)
    if($value -notmatch '^[0-9a-fA-F]{40}$'){ throw "Updater installed-client E2E evidence property '$Name' was not a 40-hex source SHA." }
    return $value.ToLowerInvariant()
  }
  $requiredHash={
    param($Object,[string]$Name)
    $value=[string](& $requiredProperty $Object $Name)
    if($value -notmatch '^[0-9a-fA-F]{64}$'){ throw "Updater installed-client E2E sentinel '$Name' was not a SHA-256 digest." }
    return $value.ToUpperInvariant()
  }

  $schemaVersion=[int](& $requiredProperty $doc 'schemaVersion')
  if($schemaVersion -ne 1){ throw "Unsupported updater installed-client E2E evidence schema version '$schemaVersion'." }
  if($ExpectedTargetSourceSha -notmatch '^[0-9a-fA-F]{40}$'){
    throw 'Expected updater installed-client E2E target source was not a 40-hex SHA.'
  }
  $expectedTargetSource=$ExpectedTargetSourceSha.ToLowerInvariant()

  $oldTag=[string](& $requiredProperty $doc 'oldTag')
  $oldBuild=[long](& $requiredProperty $doc 'oldBuild')
  $oldSource=& $requiredSha $doc 'oldSource'
  $targetBuild=[long](& $requiredProperty $doc 'targetBuild')
  $targetSource=& $requiredSha $doc 'targetSource'
  if($oldTag -notmatch '^updater-main-[0-9]{1,18}$' -or (Get-UpdaterBuildFromTag -Tag $oldTag) -ne $oldBuild -or $oldBuild -lt 0 -or $targetBuild -le $oldBuild){
    throw 'Updater installed-client E2E evidence contained an invalid old/target release identity.'
  }
  if(-not [string]::Equals($targetSource,$expectedTargetSource,[StringComparison]::OrdinalIgnoreCase)){
    throw "Updater installed-client E2E target source '$targetSource' did not match exact tested source '$expectedTargetSource'."
  }

  $scenarioA=& $requiredProperty $doc 'scenarioA'
  $scenarioB=& $requiredProperty $doc 'scenarioB'
  if([string](& $requiredProperty $scenarioA 'status') -ne 'PASS'){ throw 'Updater installed-client E2E update scenario was not PASS.' }
  if([string](& $requiredProperty $scenarioB 'status') -ne 'PASS'){ throw 'Updater installed-client E2E rollback scenario was not PASS.' }

  $scenarioATargetBuild=[long](& $requiredProperty $scenarioA 'targetBuild')
  $scenarioATargetSource=& $requiredSha $scenarioA 'targetSource'
  $healthBuild=[long](& $requiredProperty $scenarioA 'healthBuild')
  $healthSource=& $requiredSha $scenarioA 'healthSource'
  $oldClientExitCode=[int](& $requiredProperty $scenarioA 'oldClientExitCode')
  $selectorDisplayText=[string](& $requiredProperty $scenarioA 'selectorDisplayText')
  $switchButtonEnabled=[bool](& $requiredProperty $scenarioA 'switchButtonEnabled')
  $settingsButtonEnabled=[bool](& $requiredProperty $scenarioA 'settingsButtonEnabled')
  $scenarioAJournalPhase=[string](& $requiredProperty $scenarioA 'journalPhase')
  if($scenarioATargetBuild -ne $targetBuild -or
     -not [string]::Equals($scenarioATargetSource,$targetSource,[StringComparison]::OrdinalIgnoreCase) -or
     $healthBuild -ne $targetBuild -or
     -not [string]::Equals($healthSource,$targetSource,[StringComparison]::OrdinalIgnoreCase) -or
     $oldClientExitCode -ne 0 -or
     $selectorDisplayText -ne 'Updater E2E Fake Game' -or
     -not $switchButtonEnabled -or
     -not $settingsButtonEnabled -or
     $scenarioAJournalPhase -ne 'Confirmed'){
    throw 'Updater installed-client E2E update evidence failed durable-attestation validation.'
  }

  $restoredBuild=[long](& $requiredProperty $scenarioB 'restoredBuild')
  $restoredSource=& $requiredSha $scenarioB 'restoredSource'
  $oldOwnedFileCount=[long](& $requiredProperty $scenarioB 'oldOwnedFileCount')
  $targetOnlyFileCount=[long](& $requiredProperty $scenarioB 'targetOnlyFileCount')
  $scenarioBJournalPhase=[string](& $requiredProperty $scenarioB 'journalPhase')
  if($restoredBuild -ne $oldBuild -or
     -not [string]::Equals($restoredSource,$oldSource,[StringComparison]::OrdinalIgnoreCase) -or
     $oldOwnedFileCount -lt 0 -or
     $targetOnlyFileCount -lt 0 -or
     $scenarioBJournalPhase -ne 'RolledBack'){
    throw 'Updater installed-client E2E rollback evidence failed durable-attestation validation.'
  }

  $scenarioASentinels=& $requiredProperty $scenarioA 'sentinelSha256'
  $scenarioBSentinels=& $requiredProperty $scenarioB 'sentinelSha256'
  $sentinelNames=[ordered]@{
    mod='Mods/e2e-user.mod'
    state='State/e2e-state.json'
    unknownUserFile='e2e-unknown-user-file.txt'
  }
  $scenarioAHashes=[ordered]@{}
  $scenarioBHashes=[ordered]@{}
  foreach($entry in $sentinelNames.GetEnumerator()){
    $scenarioAHashes[$entry.Key]=& $requiredHash $scenarioASentinels $entry.Value
    $scenarioBHashes[$entry.Key]=& $requiredHash $scenarioBSentinels $entry.Value
  }

  $durable=[ordered]@{
    schema='mhw-mod-manager/updater-installed-client-e2e-durable/v1'
    sourceEvidenceSchemaVersion=$schemaVersion
    oldRelease=[ordered]@{tag=$oldTag;build=$oldBuild;source=$oldSource}
    target=[ordered]@{build=$targetBuild;source=$targetSource}
    update=[ordered]@{
      status='PASS'
      targetBuild=$scenarioATargetBuild
      targetSource=$scenarioATargetSource
      oldClientExitCode=$oldClientExitCode
      healthBuild=$healthBuild
      healthSource=$healthSource
      selectorDisplayText='Updater E2E Fake Game'
      switchButtonEnabled=$true
      settingsButtonEnabled=$true
      journalPhase='Confirmed'
      sentinelSha256=$scenarioAHashes
    }
    rollback=[ordered]@{
      status='PASS'
      journalPhase='RolledBack'
      restoredBuild=$restoredBuild
      restoredSource=$restoredSource
      oldOwnedFileCount=$oldOwnedFileCount
      targetOnlyFileCount=$targetOnlyFileCount
      sentinelSha256=$scenarioBHashes
    }
  }
  return ($durable | ConvertTo-Json -Depth 8)
}

function Get-UpdaterPublicationDecision {
  param(
    [Parameter(Mandatory=$true)][long]$CurrentBuild,
    [Parameter(Mandatory=$true)][string]$CurrentSourceSha,
    [Parameter(Mandatory=$true)][string]$RemoteMainSha,

    [long]$PreviousBuild=0,
    [string]$PreviousSourceSha='',
    [string[]]$ChangedPaths=@(),
    [string[]]$RemoteMainChangedPaths=@()
  )
  if($CurrentBuild -le 0){throw 'Current updater build number must be positive.'}
  $drift=Get-UpdaterMainDriftDecision -CurrentSourceSha $CurrentSourceSha -RemoteMainSha $RemoteMainSha -ChangedPaths $RemoteMainChangedPaths
  if(-not $drift.Publish){
    return [pscustomobject]@{Publish=$false;Reason=$drift.Reason}
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
