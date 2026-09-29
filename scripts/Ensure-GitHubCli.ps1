$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest

if(Get-Command gh -ErrorAction SilentlyContinue){
  gh --version
  if($LASTEXITCODE -ne 0){throw 'Installed GitHub CLI failed its version probe.'}
  exit 0
}
if([string]::IsNullOrWhiteSpace($env:GH_TOKEN)){throw 'GH_TOKEN is required to bootstrap GitHub CLI.'}

$headers=@{
  Accept='application/vnd.github+json'
  Authorization="Bearer $env:GH_TOKEN"
  'X-GitHub-Api-Version'='2022-11-28'
  'User-Agent'='MHW-Mod-Manager-Release-Gate'
}
try {
  $latest=Invoke-RestMethod -UseBasicParsing -Method Get -Headers $headers -Uri 'https://api.github.com/repos/cli/cli/releases/latest' -TimeoutSec 60
} finally {
  $headers.Authorization=$null
}

$assets=@($latest.assets | Where-Object { [string]$_.name -match '^gh_[0-9.]+_windows_amd64\.zip$' })
if($assets.Count -ne 1){throw "Could not resolve exactly one official portable Windows GitHub CLI asset; found $($assets.Count)."}
$asset=$assets[0]
$expectedDigest=[string]$asset.digest
if($expectedDigest -notmatch '^sha256:[0-9a-fA-F]{64}$'){throw 'Official GitHub CLI asset is missing a trustworthy SHA-256 digest.'}

$tempRoot=if(-not [string]::IsNullOrWhiteSpace($env:RUNNER_TEMP)){$env:RUNNER_TEMP}else{[IO.Path]::GetTempPath()}
$zip=Join-Path $tempRoot 'mhw-gh-cli.zip'
$toolRoot=Join-Path $tempRoot 'mhw-gh-cli'
Remove-Item -LiteralPath $zip -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $toolRoot -Recurse -Force -ErrorAction SilentlyContinue

try {
  Invoke-WebRequest -UseBasicParsing -Headers @{ 'User-Agent'='MHW-Mod-Manager-Release-Gate' } -Uri ([string]$asset.browser_download_url) -OutFile $zip -TimeoutSec 120
  $actualDigest='sha256:'+(Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
  if($actualDigest -ne $expectedDigest.ToLowerInvariant()){
    throw "Portable GitHub CLI digest mismatch: expected $expectedDigest, got $actualDigest."
  }
  Expand-Archive -LiteralPath $zip -DestinationPath $toolRoot -Force
} finally {
  Remove-Item -LiteralPath $zip -Force -ErrorAction SilentlyContinue
}

$executables=@(Get-ChildItem -LiteralPath $toolRoot -Filter 'gh.exe' -Recurse -File)
if($executables.Count -ne 1){throw "Downloaded GitHub CLI archive contained $($executables.Count) gh.exe files; expected exactly one."}
$ghDir=$executables[0].Directory.FullName
$env:PATH="$ghDir;$env:PATH"
if(-not [string]::IsNullOrWhiteSpace($env:GITHUB_PATH)){
  $utf8NoBom=New-Object System.Text.UTF8Encoding($false)
  [IO.File]::AppendAllText($env:GITHUB_PATH,($ghDir+[Environment]::NewLine),$utf8NoBom)
}

& $executables[0].FullName --version
if($LASTEXITCODE -ne 0){throw 'Bootstrapped GitHub CLI failed its version probe.'}
Write-Host "PASS: bootstrapped verified portable GitHub CLI $($latest.tag_name)." -ForegroundColor Green
