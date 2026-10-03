Set-StrictMode -Version Latest

function New-PublicReleaseProvenanceRecord {
  [CmdletBinding()]
  param(
    [Parameter(Mandatory=$true)][string]$Version,
    [Parameter(Mandatory=$true)][long]$Build,
    [Parameter(Mandatory=$true)][string]$SourceSha,
    [Parameter(Mandatory=$true)][string]$Tag,
    [Parameter(Mandatory=$true)][ValidateSet('stable','beta','preview','internal')][string]$Channel,
    [Parameter(Mandatory=$true)][DateTimeOffset]$PublishedUtc,
    [Parameter(Mandatory=$true)][string[]]$ArtifactPaths,
    [string]$SignedMetadataKeyId=''
  )

  if($Version -notmatch '^[0-9]+(?:\.[0-9]+){2,3}(?:[-+][0-9A-Za-z.-]+)?$'){
    throw "Public release provenance version '$Version' is malformed."
  }
  if($Build -lt 0){
    throw 'Public release provenance build may not be negative.'
  }
  if($SourceSha -cnotmatch '^[0-9a-f]{40}$'){
    throw 'Public release provenance source SHA must be exactly 40 lowercase hexadecimal characters.'
  }
  if([string]::IsNullOrWhiteSpace($Tag)){
    throw 'Public release provenance tag may not be blank.'
  }
  $artifactPathItems=@($ArtifactPaths)
  if($artifactPathItems.Count -lt 1){
    throw 'Public release provenance requires at least one artifact path.'
  }
  if(-not [string]::IsNullOrWhiteSpace($SignedMetadataKeyId) -and
     ($SignedMetadataKeyId.Contains([char]13) -or $SignedMetadataKeyId.Contains([char]10))){
    throw 'Public release provenance signing key id may not contain newlines.'
  }

  $seen=New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
  $artifacts=New-Object System.Collections.Generic.List[object]

  foreach($inputPath in $artifactPathItems){
    if([string]::IsNullOrWhiteSpace($inputPath)){
      throw 'Public release provenance artifact path may not be blank.'
    }

    $resolvedItems=@(Resolve-Path -LiteralPath $inputPath -ErrorAction Stop)
    if($resolvedItems.Count -ne 1){
      throw "Artifact path '$inputPath' resolved to $($resolvedItems.Count) entries; expected exactly one file."
    }

    $item=Get-Item -LiteralPath $resolvedItems[0].Path -Force -ErrorAction Stop
    if($item.PSIsContainer){
      throw "Artifact path '$inputPath' is a directory."
    }
    if(($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0){
      throw "Artifact path '$inputPath' is a reparse point and is not accepted for provenance hashing."
    }

    $name=$item.Name
    if([string]::IsNullOrWhiteSpace($name) -or $name.Contains('/') -or $name.Contains('\')){
      throw "Artifact path '$inputPath' did not resolve to a plain artifact filename."
    }
    if(-not $seen.Add($name)){
      throw "Duplicate public release artifact filename '$name'."
    }

    $hash=(Get-FileHash -LiteralPath $item.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    $entry=[ordered]@{
      name=$name
      size_bytes=[long]$item.Length
      sha256=$hash
    }
    if(-not [string]::IsNullOrWhiteSpace($SignedMetadataKeyId)){
      $entry.signed_metadata_key_id=$SignedMetadataKeyId.Trim()
    }
    $artifacts.Add([pscustomobject]$entry)
  }

  $sortedArtifacts=@($artifacts | Sort-Object -Property name)
  return [pscustomobject][ordered]@{
    version=$Version
    build=$Build
    source_repository='fengie/mhw-mods'
    source_sha=$SourceSha
    tag=$Tag
    channel=$Channel
    published_at_utc=$PublishedUtc.ToUniversalTime().ToString(
      'yyyy-MM-ddTHH:mm:ssZ',
      [Globalization.CultureInfo]::InvariantCulture)
    artifacts=$sortedArtifacts
  }
}

function ConvertTo-PublicReleaseProvenanceJson {
  [CmdletBinding()]
  param(
    [Parameter(Mandatory=$true)][object]$Record
  )

  return ($Record | ConvertTo-Json -Depth 8)
}


function Test-PublicReleaseProvenanceRecordEquivalent {
  [CmdletBinding()]
  param(
    [Parameter(Mandatory=$true)]$Left,
    [Parameter(Mandatory=$true)]$Right
  )

  foreach($name in @('version','build','source_repository','source_sha','tag','channel','published_at_utc')){
    $leftProp=$Left.PSObject.Properties[$name]
    $rightProp=$Right.PSObject.Properties[$name]
    if($null -eq $leftProp -or $null -eq $rightProp){return $false}
    if([string]$leftProp.Value -cne [string]$rightProp.Value){return $false}
  }

  $leftArtifacts=@($Left.artifacts | Sort-Object -Property name)
  $rightArtifacts=@($Right.artifacts | Sort-Object -Property name)
  if($leftArtifacts.Count -ne $rightArtifacts.Count){return $false}
  for($i=0;$i -lt $leftArtifacts.Count;$i++){
    foreach($name in @('name','size_bytes','sha256')){
      $leftProp=$leftArtifacts[$i].PSObject.Properties[$name]
      $rightProp=$rightArtifacts[$i].PSObject.Properties[$name]
      if($null -eq $leftProp -or $null -eq $rightProp){return $false}
      if([string]$leftProp.Value -cne [string]$rightProp.Value){return $false}
    }
    $leftKey=$leftArtifacts[$i].PSObject.Properties['signed_metadata_key_id']
    $rightKey=$rightArtifacts[$i].PSObject.Properties['signed_metadata_key_id']
    $leftKeyValue=if($null -eq $leftKey){''}else{[string]$leftKey.Value}
    $rightKeyValue=if($null -eq $rightKey){''}else{[string]$rightKey.Value}
    if($leftKeyValue -cne $rightKeyValue){return $false}
  }
  return $true
}

function Add-PublicReleaseProvenanceRecordToIndexJson {
  [CmdletBinding()]
  param(
    [Parameter(Mandatory=$true)][string]$IndexJson,
    [Parameter(Mandatory=$true)]$Record
  )

  if([string]::IsNullOrWhiteSpace($IndexJson)){
    throw 'Public release provenance index was empty.'
  }
  try{
    $index=ConvertFrom-Json -InputObject $IndexJson -ErrorAction Stop
  }catch{
    throw 'Public release provenance index contained invalid JSON.'
  }
  if($null -eq $index -or $null -eq $index.PSObject.Properties['schema_version'] -or [int]$index.schema_version -ne 1){
    throw 'Public release provenance index schema_version must be 1.'
  }
  if($null -eq $index.PSObject.Properties['releases']){
    throw 'Public release provenance index omitted releases.'
  }

  $releases=@($index.releases)
  $identity="$([string]$Record.version)|$([long]$Record.build)|$([string]$Record.tag)"
  $source=[string]$Record.source_sha
  foreach($existing in $releases){
    if($null -eq $existing){throw 'Public release provenance index contained a null release.'}
    $existingIdentity="$([string]$existing.version)|$([long]$existing.build)|$([string]$existing.tag)"
    if($existingIdentity -ceq $identity){
      if(Test-PublicReleaseProvenanceRecordEquivalent -Left $existing -Right $Record){
        return [pscustomobject]@{Added=$false;Json=$IndexJson;Identity=$identity}
      }
      throw "Public release provenance index already contains conflicting immutable identity $identity."
    }
    if([string]$existing.source_sha -ceq $source){
      throw "Public release provenance index already binds source SHA $source to a different immutable release."
    }
  }

  $next=[ordered]@{
    schema_version=1
    releases=@($releases)+@($Record)
  }
  $json=($next | ConvertTo-Json -Depth 10)
  if(-not $json.EndsWith([Environment]::NewLine,[StringComparison]::Ordinal)){
    $json += [Environment]::NewLine
  }
  return [pscustomobject]@{Added=$true;Json=$json;Identity=$identity}
}
