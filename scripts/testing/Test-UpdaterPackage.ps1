param(
  [Parameter(Mandatory=$true)][string]$ArtifactPath,
  [Parameter(Mandatory=$true)][string]$ManifestPath,
  [string]$ExpectedSourceSha='',
  [long]$ExpectedBuildNumber=0
)
$ErrorActionPreference='Stop'

$artifact=(Resolve-Path -LiteralPath $ArtifactPath).Path
$manifestFile=(Resolve-Path -LiteralPath $ManifestPath).Path
$manifestBytes=[IO.File]::ReadAllBytes($manifestFile)
if($manifestBytes.Length -ge 3 -and $manifestBytes[0] -eq 0xEF -and $manifestBytes[1] -eq 0xBB -and $manifestBytes[2] -eq 0xBF){
  throw 'update-manifest.json must be UTF-8 without a BOM so existing updater clients can parse it.'
}
$manifest=Get-Content -LiteralPath $manifestFile -Raw | ConvertFrom-Json

if([int]$manifest.schemaVersion -ne 1){throw "Unsupported update manifest schema: $($manifest.schemaVersion)"}
if([string]$manifest.channel -ne 'main'){throw "Unexpected update channel: $($manifest.channel)"}
if([string]::IsNullOrWhiteSpace([string]$manifest.productVersion)){throw 'Missing productVersion.'}
if([long]$manifest.buildNumber -le 0){throw 'buildNumber must be positive.'}
if($ExpectedBuildNumber -gt 0 -and [long]$manifest.buildNumber -ne $ExpectedBuildNumber){
  throw "Build number mismatch. Expected $ExpectedBuildNumber, got $($manifest.buildNumber)."
}
if(-not [string]::IsNullOrWhiteSpace($ExpectedSourceSha) -and [string]$manifest.sourceSha -ne $ExpectedSourceSha){
  throw "Source SHA mismatch. Expected $ExpectedSourceSha, got $($manifest.sourceSha)."
}
if([string]$manifest.artifactName -ne [IO.Path]::GetFileName($artifact)){
  throw "Artifact name mismatch: manifest=$($manifest.artifactName) actual=$([IO.Path]::GetFileName($artifact))"
}
$artifactInfo=Get-Item -LiteralPath $artifact
if([long]$manifest.artifactSize -ne [long]$artifactInfo.Length){
  throw "Artifact size mismatch: manifest=$($manifest.artifactSize) actual=$($artifactInfo.Length)"
}
$artifactHash=(Get-FileHash -LiteralPath $artifact -Algorithm SHA256).Hash
if([string]$manifest.sha256 -ne $artifactHash){
  throw "Artifact SHA-256 mismatch: manifest=$($manifest.sha256) actual=$artifactHash"
}
if([int]$manifest.minimumUpdaterProtocol -gt 1){throw "Unsupported minimum updater protocol: $($manifest.minimumUpdaterProtocol)"}
if([string]::IsNullOrWhiteSpace([string]$manifest.executableRelativePath)){throw 'Missing executableRelativePath.'}

$temp=Join-Path ([IO.Path]::GetTempPath()) ('mhwmm-update-package-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force $temp | Out-Null
try {
  Add-Type -AssemblyName System.IO.Compression.FileSystem
  [IO.Compression.ZipFile]::ExtractToDirectory($artifact,$temp)

  $productPath=Join-Path $temp 'product-files.json'
  $markerPath=Join-Path $temp 'release-install.json'
  $identityPath=Join-Path $temp 'build-identity.json'
  foreach($required in @($productPath,$markerPath,$identityPath)){
    if(-not (Test-Path -LiteralPath $required -PathType Leaf)){throw "Required updater package file missing: $required"}
  }

  $productHash=(Get-FileHash -LiteralPath $productPath -Algorithm SHA256).Hash
  if([string]$manifest.productManifestSha256 -ne $productHash){
    throw "Product manifest SHA-256 mismatch: update=$($manifest.productManifestSha256) actual=$productHash"
  }

  $product=Get-Content -LiteralPath $productPath -Raw | ConvertFrom-Json
  $marker=Get-Content -LiteralPath $markerPath -Raw | ConvertFrom-Json
  $identity=Get-Content -LiteralPath $identityPath -Raw | ConvertFrom-Json

  if([int]$product.schemaVersion -ne 1){throw 'Unsupported product manifest schema.'}
  if([int]$marker.schemaVersion -ne 1){throw 'Unsupported install marker schema.'}
  if([int]$identity.schemaVersion -ne 1){throw 'Unsupported build identity schema.'}
  if([string]$marker.productId -ne 'fengie/mhw-mods:MHW-Manual-Mod-Manager'){throw 'Unexpected install marker productId.'}
  if([string]$marker.channel -ne 'main' -or [string]$identity.channel -ne 'main'){throw 'Update package channel mismatch.'}

  foreach($pair in @(
    @('productVersion',[string]$manifest.productVersion,[string]$identity.productVersion),
    @('sourceSha',[string]$manifest.sourceSha,[string]$identity.sourceSha),
    @('sourceSha(marker)',[string]$manifest.sourceSha,[string]$marker.build.sourceSha),
    @('productVersion(marker)',[string]$manifest.productVersion,[string]$marker.build.productVersion),
    @('executableRelativePath',[string]$manifest.executableRelativePath,[string]$marker.executableRelativePath),
    @('productManifestSha256',[string]$manifest.productManifestSha256,[string]$marker.productManifestSha256)
  )){
    if($pair[1] -ne $pair[2]){throw "$($pair[0]) mismatch: expected=$($pair[1]) actual=$($pair[2])"}
  }
  if([long]$manifest.buildNumber -ne [long]$identity.buildNumber -or
     [long]$manifest.buildNumber -ne [long]$marker.build.buildNumber){
    throw 'Build number mismatch across update manifest, build identity, and install marker.'
  }

  $expected=New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
  [void]$expected.Add('product-files.json')
  [void]$expected.Add('release-install.json')
  $protected=@('Mods','State','Inbox','Mods Archive','Games','Support Bundles','BuildLogs')
  $hasHelper=$false
  $hasExecutable=$false
  foreach($entry in @($product.files)){
    $rel=([string]$entry.path).Replace('\','/')
    if([string]::IsNullOrWhiteSpace($rel) -or $rel.StartsWith('/') -or $rel.Contains(':') -or $rel.Contains('../')){
      throw "Unsafe product path in package manifest: $rel"
    }
    $first=$rel.Split('/')[0]
    if($protected -contains $first){throw "Product manifest claims protected user/runtime root: $rel"}
    if($rel -ieq 'MHW-DEBUG-ALL.log'){throw 'Mutable master debug log must not be updater-owned.'}
    if(-not $expected.Add($rel)){throw "Duplicate product manifest path: $rel"}

    $file=Join-Path $temp ($rel.Replace('/',[IO.Path]::DirectorySeparatorChar))
    if(-not (Test-Path -LiteralPath $file -PathType Leaf)){throw "Product-owned file missing from ZIP: $rel"}
    $info=Get-Item -LiteralPath $file
    if([long]$entry.size -ne [long]$info.Length){throw "Product file size mismatch: $rel"}
    $hash=(Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash
    if([string]$entry.sha256 -ne $hash){throw "Product file SHA-256 mismatch: $rel"}
    if($rel -ieq 'UpdaterHelper/MHW Mod Manager Updater.exe'){$hasHelper=$true}
    if($rel -ieq [string]$manifest.executableRelativePath){$hasExecutable=$true}
  }
  if(-not $hasHelper){throw 'Verified updater helper is not owned by product-files.json.'}
  if(-not $hasExecutable){throw 'Restart executable is not owned by product-files.json.'}

  $actual=New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
  foreach($file in Get-ChildItem -LiteralPath $temp -Recurse -File){
    $rel=$file.FullName.Substring($temp.Length).TrimStart('\','/').Replace('\','/')
    [void]$actual.Add($rel)
  }
  if(-not $actual.SetEquals($expected)){
    $unexpected=@($actual | Where-Object {-not $expected.Contains($_)})
    $missing=@($expected | Where-Object {-not $actual.Contains($_)})
    throw "ZIP file set does not match product ownership. Unexpected=[$($unexpected -join ', ')] Missing=[$($missing -join ', ')]"
  }

  Write-Host ("PASS: updater package build={0} source={1} artifact={2} sha256={3}" -f
    $manifest.buildNumber,$manifest.sourceSha,$manifest.artifactName,$artifactHash) -ForegroundColor Green
}
finally {
  Remove-Item -LiteralPath $temp -Recurse -Force -ErrorAction SilentlyContinue
}
