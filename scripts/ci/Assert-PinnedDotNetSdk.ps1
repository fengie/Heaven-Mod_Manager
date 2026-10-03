param([string]$Root='')
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest

if([string]::IsNullOrWhiteSpace($Root)){
  $Root=(Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
}else{
  $Root=(Resolve-Path -LiteralPath $Root).Path
}

$globalJsonPath=Join-Path $Root 'global.json'
if(-not (Test-Path -LiteralPath $globalJsonPath -PathType Leaf)){
  throw "Repository SDK pin is missing: $globalJsonPath"
}

try{
  $globalJson=Get-Content -LiteralPath $globalJsonPath -Raw | ConvertFrom-Json -ErrorAction Stop
}catch{
  throw "Repository SDK pin is invalid JSON: $globalJsonPath"
}

$expected=[string]$globalJson.sdk.version
if($expected -notmatch '^[0-9]+\.[0-9]+\.[0-9]+(?:[-+][0-9A-Za-z.-]+)?$'){
  throw "global.json sdk.version '$expected' is missing or malformed."
}

$dotnet=(Get-Command dotnet -ErrorAction Stop).Source
$actual=(& $dotnet --version).Trim()
if($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($actual)){
  throw 'dotnet --version failed after SDK provisioning.'
}
if(-not [string]::Equals($actual,$expected,[StringComparison]::Ordinal)){
  throw "Resolved .NET SDK '$actual' does not match repository pin '$expected'."
}

Write-Host "PASS: resolved .NET SDK $actual matches global.json." -ForegroundColor Green
