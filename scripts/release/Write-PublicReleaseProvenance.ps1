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
  if($ArtifactPaths.Count -lt 1){
    throw 'Public release provenance requires at least one artifact path.'
  }
  if(-not [string]::IsNullOrWhiteSpace($SignedMetadataKeyId) -and
     ($SignedMetadataKeyId.Contains([char]13) -or $SignedMetadataKeyId.Contains([char]10))){
    throw 'Public release provenance signing key id may not contain newlines.'
  }

  $seen=New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
  $artifacts=New-Object System.Collections.Generic.List[object]

  foreach($inputPath in $ArtifactPaths){
    if([string]::IsNullOrWhiteSpace($inputPath)){
      throw 'Public release provenance artifact path may not be blank.'
    }

    $resolved=Resolve-Path -LiteralPath $inputPath -ErrorAction Stop
    if($resolved.Count -ne 1){
      throw "Artifact path '$inputPath' resolved to $($resolved.Count) entries; expected exactly one file."
    }

    $item=Get-Item -LiteralPath $resolved.Path -Force -ErrorAction Stop
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
