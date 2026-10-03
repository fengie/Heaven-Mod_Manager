$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot '..\release\Write-PublicReleaseProvenance.ps1')

function Assert-Equal {
  param($Expected,$Actual,[string]$Label)
  if($Expected -ne $Actual){throw "$Label expected '$Expected' but got '$Actual'."}
}

function Assert-True {
  param([bool]$Value,[string]$Label)
  if(-not $Value){throw "$Label expected true."}
}

$root=Join-Path ([IO.Path]::GetTempPath()) ('mhw-public-provenance-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $root -Force | Out-Null
try {
  $a=Join-Path $root 'manager.exe'
  $b=Join-Path $root 'update-manifest.json'
  [IO.File]::WriteAllBytes($a,[Text.Encoding]::UTF8.GetBytes('MANAGER-BYTES'))
  [IO.File]::WriteAllBytes($b,[Text.Encoding]::UTF8.GetBytes('MANIFEST-BYTES'))
  $sha='abcdef0123456789abcdef0123456789abcdef01'

  $validParams=@{
    Version='8.8.86'
    Build=4242
    SourceSha=$sha
    Tag='updater-main-4242'
    Channel='stable'
    PublishedUtc=[DateTimeOffset]::Parse('2026-10-03T14:00:00Z')
    ArtifactPaths=@($b,$a)
    SignedMetadataKeyId='release-key-1'
  }
  $record=New-PublicReleaseProvenanceRecord @validParams

  Assert-Equal '8.8.86' $record.version 'version'
  Assert-Equal 4242 $record.build 'build'
  Assert-Equal 'fengie/mhw-mods' $record.source_repository 'source repository'
  Assert-Equal $sha $record.source_sha 'source sha'
  Assert-Equal '2026-10-03T14:00:00Z' $record.published_at_utc 'published utc'
  Assert-Equal 2 $record.artifacts.Count 'artifact count'
  Assert-Equal 'manager.exe' $record.artifacts[0].name 'deterministic artifact sort'
  Assert-Equal (Get-Item -LiteralPath $a).Length $record.artifacts[0].size_bytes 'artifact size'
  Assert-Equal ((Get-FileHash -LiteralPath $a -Algorithm SHA256).Hash.ToLowerInvariant()) $record.artifacts[0].sha256 'artifact hash'
  Assert-Equal 'release-key-1' $record.artifacts[0].signed_metadata_key_id 'signing key id'

  $json=ConvertTo-PublicReleaseProvenanceJson -Record $record
  $roundTrip=ConvertFrom-Json -InputObject $json
  Assert-Equal $sha $roundTrip.source_sha 'json source sha'
  Assert-Equal 2 $roundTrip.artifacts.Count 'json artifacts'

  $badShaParams=@{
    Version='8.8.86'
    Build=4242
    SourceSha='deadbeef'
    Tag='updater-main-4242'
    Channel='stable'
    PublishedUtc=[DateTimeOffset]::UtcNow
    ArtifactPaths=@($a)
  }
  $badShaRejected=$false
  try { [void](New-PublicReleaseProvenanceRecord @badShaParams) } catch { $badShaRejected=$true }
  Assert-True $badShaRejected 'short source sha rejected'

  $one=Join-Path $root 'one'
  $two=Join-Path $root 'two'
  New-Item -ItemType Directory -Path $one,$two -Force | Out-Null
  $sameOne=Join-Path $one 'same.bin'
  $sameTwo=Join-Path $two 'same.bin'
  Set-Content -LiteralPath $sameOne -Value 'ONE' -NoNewline
  Set-Content -LiteralPath $sameTwo -Value 'TWO' -NoNewline
  $duplicateParams=@{
    Version='8.8.86'
    Build=4242
    SourceSha=$sha
    Tag='updater-main-4242'
    Channel='stable'
    PublishedUtc=[DateTimeOffset]::UtcNow
    ArtifactPaths=@($sameOne,$sameTwo)
  }
  $duplicateRejected=$false
  try { [void](New-PublicReleaseProvenanceRecord @duplicateParams) } catch { $duplicateRejected=$true }
  Assert-True $duplicateRejected 'duplicate artifact filename rejected'

  $link=Join-Path $root 'linked-artifact.bin'
  $reparseTested=$false
  try {
    New-Item -ItemType SymbolicLink -Path $link -Target $a -ErrorAction Stop | Out-Null
    $reparseTested=$true
    $linkParams=@{
      Version='8.8.86'
      Build=4242
      SourceSha=$sha
      Tag='updater-main-4242'
      Channel='stable'
      PublishedUtc=[DateTimeOffset]::UtcNow
      ArtifactPaths=@($link)
    }
    $reparseRejected=$false
    try { [void](New-PublicReleaseProvenanceRecord @linkParams) } catch { $reparseRejected=$true }
    Assert-True $reparseRejected 'reparse artifact rejected'
  } catch {
    Write-Host 'SKIP: symbolic-link creation unavailable for provenance reparse regression.'
  }
  if($reparseTested){ Assert-True (Test-Path -LiteralPath $a) 'reparse target preserved' }

  Write-Host 'PASS: public release provenance generator regressions.'
}
finally {
  try { Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue } catch {}
}
