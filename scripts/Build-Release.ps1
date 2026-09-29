$ErrorActionPreference='Stop'
$Root=(Resolve-Path (Join-Path $PSScriptRoot '..')).Path
. (Join-Path $PSScriptRoot 'Master-Debug.ps1')
$MasterDebug=Get-MhwMasterDebugPath -Root $Root
Start-MhwMasterDebugSession -Root $Root -Area 'BUILD' -Title 'Release build'
$buildSucceeded=$false
$publishForMasterCopy=$null
$dotnet=(Get-Command dotnet -ErrorAction Stop).Source
$version=& $dotnet --version
if([version]$version -lt [version]'10.0.401'){throw "Install the .NET 10.0.401 or newer SDK. Found: $version"}
$isWindowsHost = [Environment]::OSVersion.Platform -eq [PlatformID]::Win32NT
if(-not $isWindowsHost){throw 'The production WPF release must be built and integration-tested on Windows.'}

$logRoot=Join-Path $Root 'BuildLogs'
New-Item -ItemType Directory -Force $logRoot|Out-Null
$stamp=Get-Date -Format 'yyyyMMdd-HHmmss'
$masterLog=Join-Path $logRoot ("build-release-"+$stamp+".log")
$reportPath=Join-Path $logRoot ("build-release-report-"+$stamp+".txt")
$functionReport=Join-Path $logRoot ("build-function-verification-"+$stamp+".json")
$functionConfirmReport=Join-Path $logRoot ("build-function-verification-confirm-"+$stamp+".json")
$functionBaseline=Join-Path $Root ".verification\function-status.json"
$trustedFunctionFiles=Join-Path $Root ".verification\trusted-v8.7.0-files.json"
$trustedFunctionSource=Join-Path $Root ".verification\trusted-v8.7.0-src.zip"
$transcriptStarted=$false
try{Start-Transcript -Path $masterLog -Force|Out-Null;$transcriptStarted=$true}catch{}

function Invoke-DotNetStage {
  param(
    [Parameter(Mandatory=$true)][string]$Name,
    [Parameter(Mandatory=$true)][string[]]$Arguments,
    [Parameter(Mandatory=$true)][string]$LogPath
  )
  Write-Host ""
  Write-Host ("=== "+$Name+" ===") -ForegroundColor Cyan
  Write-Host ("Log: "+$LogPath) -ForegroundColor DarkGray
  Write-MhwMasterDebug -Root $Root -Area 'BUILD-STAGE' -Message ("START: "+$Name+" | DetailLog="+$LogPath)
  $old=$ErrorActionPreference
  $ErrorActionPreference='Continue'
  try{
    # Tee-Object writes its input back to the success pipeline. If this function is
    # called in an assignment, that would contaminate the returned exit code with
    # every line of dotnet output. Out-Host consumes the tee'd output so the only
    # success-pipeline value returned by this function is the scalar integer below.
    & $dotnet @Arguments 2>&1 | Tee-Object -FilePath $LogPath | ForEach-Object {
      $line=$_.ToString()
      Write-Host $line
      Write-MhwMasterDebug -Root $Root -Area 'DOTNET' -Message ("["+$Name+"] "+$line)
    }
    $code=[int]$LASTEXITCODE
  } finally {
    $ErrorActionPreference=$old
  }
  if($code -eq 0){Write-Host ("PASS: "+$Name) -ForegroundColor Green;Write-MhwMasterDebug -Root $Root -Area 'BUILD-STAGE' -Message ("PASS: "+$Name+" | ExitCode="+$code)}
  else{
    Write-Host ("FAIL: "+$Name+" (exit "+$code+")") -ForegroundColor Red
    Write-MhwMasterDebug -Root $Root -Area 'BUILD-STAGE' -Message ("FAIL: "+$Name+" | ExitCode="+$code+" | DetailLog="+$LogPath)
    $diag=@(Get-Content $LogPath -ErrorAction SilentlyContinue | Where-Object {$_ -match '(?i)\b(error|failed|NETSDK\d+|MSB\d+|CS\d+|CA\d+)\b'} | Select-Object -First 24)
    if($diag.Count -gt 0){Write-Host 'First diagnostics:' -ForegroundColor Yellow;$diag|ForEach-Object{Write-Host ('  '+$_);Write-MhwMasterDebug -Root $Root -Area 'BUILD-DIAGNOSTIC' -Message ("["+$Name+"] "+$_.ToString())}}
  }
  return [int]$code
}

function Assert-ScalarExitCode {
  param([Parameter(Mandatory=$true)]$Code,[Parameter(Mandatory=$true)][string]$StageName)
  if($Code -is [array]){
    throw "Build harness contract failure: '$StageName' returned an array instead of one exit code."
  }
  try{[void][int]$Code}catch{
    throw "Build harness contract failure: '$StageName' returned a non-integer exit code: $Code"
  }
}

function Require-Stage {
  param([string]$Name,[string[]]$Arguments,[string]$LogPath)
  $code=Invoke-DotNetStage -Name $Name -Arguments $Arguments -LogPath $LogPath
  Assert-ScalarExitCode -Code $code -StageName $Name
  $code=[int]$code
  if($code -ne 0){throw "$Name failed (exit $code). See $LogPath"}
}

function New-DeterministicZip {
  param([string]$SourceRoot,[string]$Destination,[DateTimeOffset]$Timestamp)
  Add-Type -AssemblyName System.IO.Compression
  $resolved=(Resolve-Path -LiteralPath $SourceRoot).Path
  Remove-Item -LiteralPath $Destination -Force -ErrorAction SilentlyContinue
  $stream=[IO.File]::Open($Destination,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None)
  try {
    $archive=[IO.Compression.ZipArchive]::new($stream,[IO.Compression.ZipArchiveMode]::Create,$true)
    try {
      foreach($file in Get-ChildItem -LiteralPath $resolved -Recurse -File | Sort-Object FullName){
        $relative=$file.FullName.Substring($resolved.Length).TrimStart('\','/').Replace('\','/')
        $entry=$archive.CreateEntry($relative,[IO.Compression.CompressionLevel]::Optimal)
        $entry.LastWriteTime=$Timestamp
        $input=$file.OpenRead()
        $output=$entry.Open()
        try {$input.CopyTo($output)} finally {$output.Dispose();$input.Dispose()}
      }
    } finally {$archive.Dispose()}
  } finally {$stream.Dispose()}
}

Push-Location $Root
try{
  Write-Host ("SDK: "+$version)
  Write-Host ("Master build transcript: "+$masterLog)
  Write-Host ("MASTER DEBUG LOG (send this file): "+$MasterDebug) -ForegroundColor Yellow
  Write-MhwMasterDebug -Root $Root -Area 'BUILD' -Message ("SDK="+$version+"; Transcript="+$masterLog+"; MasterDebug="+$MasterDebug)

  $dirtyTracked=@(& git diff HEAD --name-only -- | Where-Object {
    $normalized=$_.Replace('\','/')
    -not ($normalized.StartsWith('.verification/',[StringComparison]::OrdinalIgnoreCase) -or
      [string]::Equals($normalized,'MHW-DEBUG-ALL.log',[StringComparison]::OrdinalIgnoreCase))
  })
  $dirtyUntracked=@(& git ls-files --others --exclude-standard | Where-Object {
    $normalized=$_.Replace('\','/')
    $generated=$normalized.StartsWith('BuildLogs/',[StringComparison]::OrdinalIgnoreCase) -or
      $normalized.StartsWith('artifacts/',[StringComparison]::OrdinalIgnoreCase) -or
      $normalized.StartsWith('release/',[StringComparison]::OrdinalIgnoreCase) -or
      [string]::Equals($normalized,'MHW-DEBUG-ALL.log',[StringComparison]::OrdinalIgnoreCase)
    -not $generated
  })
  $dirtyInputs=@($dirtyTracked+$dirtyUntracked | Sort-Object -Unique)
  if($dirtyInputs.Count -gt 0){
    throw "Release build requires an exact committed checkout. Dirty paths: $($dirtyInputs -join ', ')"
  }

  & (Join-Path $PSScriptRoot 'Test-AgentHandoff.ps1') -Root $Root
  & (Join-Path $PSScriptRoot 'Test-VerificationCache.ps1')
  Require-Stage 'Solution restore' @('restore','.\MhwModManager.sln') (Join-Path $logRoot ("build-restore-"+$stamp+".log"))
  Require-Stage 'Function fingerprint scan' @('run','-c','Release','--project','.\tools\MhwModManager.FunctionVerifier\MhwModManager.FunctionVerifier.csproj','--no-restore','--','--root',$Root,'--mode','scan','--baseline',$functionBaseline,'--trusted-files',$trustedFunctionFiles,'--trusted-source',$trustedFunctionSource,'--report',$functionReport) (Join-Path $logRoot ("build-function-scan-"+$stamp+".log"))
  Require-Stage 'Solution build/analyzers' @('build','.\MhwModManager.sln','-c','Release','--no-restore','-warnaserror') (Join-Path $logRoot ("build-compile-"+$stamp+".log"))
  Require-Stage 'Core unit tests' @('test','.\tests\MhwModManager.Tests\MhwModManager.Tests.csproj','-c','Release','--no-build') (Join-Path $logRoot ("build-test-core-"+$stamp+".log"))
  Require-Stage 'Automation unit tests' @('test','.\tests\MhwModManager.AutomationTests\MhwModManager.AutomationTests.csproj','-c','Release','--no-build') (Join-Path $logRoot ("build-test-automation-"+$stamp+".log"))
  Require-Stage 'Integration/fault-injection tests' @('test','.\tests\MhwModManager.IntegrationTests\MhwModManager.IntegrationTests.csproj','-c','Release','--no-build') (Join-Path $logRoot ("build-test-integration-"+$stamp+".log"))

  $selfTestOutput = Join-Path $Root 'BuildLogs'
  Require-Stage 'Automation self-test' @('run','-c','Release','--project','.\tools\MhwModManager.SelfTest\MhwModManager.SelfTest.csproj','--no-build','--',$selfTestOutput) (Join-Path $logRoot ("build-selftest-"+$stamp+".log"))
  $appVersion=(Get-Content (Join-Path $Root 'VERSION.txt') -Raw).Trim()
  $headSha=(& git rev-parse HEAD).Trim()
  $sourceSha=$env:MHW_UPDATE_SOURCE_SHA
  if([string]::IsNullOrWhiteSpace($sourceSha)){$sourceSha=$env:GITHUB_SHA}
  if([string]::IsNullOrWhiteSpace($sourceSha)){$sourceSha=$headSha}
  if($sourceSha -notmatch '^[0-9a-fA-F]{7,64}$'){throw "Updater source SHA is malformed: $sourceSha"}
  if(-not [string]::Equals($sourceSha,$headSha,[StringComparison]::OrdinalIgnoreCase)){
    throw "Updater source SHA $sourceSha does not match checked-out HEAD $headSha."
  }
  $buildNumberText=$env:MHW_UPDATE_BUILD_NUMBER
  if([string]::IsNullOrWhiteSpace($buildNumberText)){$buildNumberText=$env:GITHUB_RUN_NUMBER}
  if([string]::IsNullOrWhiteSpace($buildNumberText)){$buildNumberText=(& git rev-list --count HEAD).Trim()}
  [long]$buildNumber=0
  if(-not [long]::TryParse($buildNumberText,[ref]$buildNumber) -or $buildNumber -le 0){
    throw "Updater build number must be a positive integer. Value: $buildNumberText"
  }
  $commitTimeText=(& git show -s --format=%cI HEAD).Trim()
  [DateTimeOffset]$commitTime=[DateTimeOffset]::Parse($commitTimeText,[Globalization.CultureInfo]::InvariantCulture)
  $buildIdentityUtc=$commitTime.ToUniversalTime().ToString('o')
  $updateChannel='main'
  $executableRelativePath='MHW Mod Manager.exe'
  Write-MhwMasterDebug -Root $Root -Area 'BUILD-UPDATER' -Message ("SourceSha="+$sourceSha+"; BuildNumber="+$buildNumber+"; ProductVersion="+$appVersion)
  $releaseName="MHW-Manual-Mod-Manager-v$appVersion"
  $publish=Join-Path $Root ("release\"+$releaseName)
  $publishForMasterCopy=$publish
  Remove-Item $publish -Recurse -Force -ErrorAction SilentlyContinue

  # ReadyToRun needs RID-specific runtime assets restored with the same property.
  # This also works around SDK regressions where a normal solution restore is insufficient.
  # Compile the exact win-x64 target with warnings-as-errors before publish.
  # This catches RID-specific analyzer/compiler diagnostics (for example CA2016)
  # before the publish phase so the failure is reported as a compile gate.
  $ridCompileLog=Join-Path $logRoot ("build-compile-win-x64-"+$stamp+".log")
  Require-Stage 'App win-x64 compile/analyzers' @('build','.\src\MhwModManager.App\MhwModManager.App.csproj','-c','Release','-r','win-x64','--self-contained','true','-warnaserror','-m:1','-p:BuildInParallel=false','-p:UseSharedCompilation=false') $ridCompileLog

  $ridRestoreLog=Join-Path $logRoot ("build-restore-win-x64-r2r-"+$stamp+".log")
  $ridRestoreCode=Invoke-DotNetStage 'win-x64 ReadyToRun restore' @('restore','.\src\MhwModManager.App\MhwModManager.App.csproj','-r','win-x64','-p:PublishReadyToRun=true') $ridRestoreLog
  Assert-ScalarExitCode -Code $ridRestoreCode -StageName 'win-x64 ReadyToRun restore'
  $ridRestoreCode=[int]$ridRestoreCode

  $publishR2RLog=Join-Path $logRoot ("build-publish-r2r-"+$stamp+".log")
  $publishCode=1
  if($ridRestoreCode -eq 0){
    $publishCode=Invoke-DotNetStage 'win-x64 self-contained publish (ReadyToRun)' @('publish','.\src\MhwModManager.App\MhwModManager.App.csproj','-c','Release','-r','win-x64','--self-contained','true','--no-restore','-p:PublishReadyToRun=true','-p:PublishSingleFile=false','-p:PublishTrimmed=false','-o',$publish) $publishR2RLog
    Assert-ScalarExitCode -Code $publishCode -StageName 'win-x64 self-contained publish (ReadyToRun)'
    $publishCode=[int]$publishCode
  }

  $usedFallback=$false
  if($publishCode -ne 0){
    $r2rText=''
    if(Test-Path $publishR2RLog){$r2rText=Get-Content $publishR2RLog -Raw -ErrorAction SilentlyContinue}
    $ridText=''
    if(Test-Path $ridRestoreLog){$ridText=Get-Content $ridRestoreLog -Raw -ErrorAction SilentlyContinue}
    $combined=$ridText+"`n"+$r2rText
    $isR2RFailure=($combined -match 'NETSDK1094|ResolveReadyToRunCompilers|ReadyToRun|crossgen')
    if(-not $isR2RFailure){throw "publish failed for a non-ReadyToRun reason. See $publishR2RLog and $ridRestoreLog"}

    Write-Warning 'ReadyToRun optimization failed in the SDK/runtime-pack phase. Retrying a functionally equivalent self-contained publish without ReadyToRun.'
    Write-MhwMasterDebug -Root $Root -Area 'BUILD-PUBLISH' -Message 'ReadyToRun failed in SDK/runtime-pack phase; starting self-contained JIT fallback.'
    $usedFallback=$true
    Remove-Item $publish -Recurse -Force -ErrorAction SilentlyContinue
    $fallbackRestoreLog=Join-Path $logRoot ("build-restore-win-x64-fallback-"+$stamp+".log")
    Require-Stage 'win-x64 fallback restore' @('restore','.\src\MhwModManager.App\MhwModManager.App.csproj','-r','win-x64','-p:PublishReadyToRun=false') $fallbackRestoreLog
    $fallbackPublishLog=Join-Path $logRoot ("build-publish-fallback-"+$stamp+".log")
    Require-Stage 'win-x64 self-contained publish (JIT fallback)' @('publish','.\src\MhwModManager.App\MhwModManager.App.csproj','-c','Release','-r','win-x64','--self-contained','true','--no-restore','-p:PublishReadyToRun=false','-p:PublishSingleFile=false','-p:PublishTrimmed=false','-o',$publish) $fallbackPublishLog
    @(
      'ReadyToRun optimization was unavailable for this local SDK/runtime-pack combination.',
      'The release was published self-contained with normal JIT compilation instead.',
      'Application behavior and features are unchanged; only ahead-of-time startup optimization is omitted.',
      'See the exact gate BuildLogs for ReadyToRun diagnostics.'
    ) | Set-Content (Join-Path $publish 'PUBLISH FALLBACK.txt') -Encoding UTF8
  }

  $helperPublish=Join-Path $publish 'UpdaterHelper'
  New-Item -ItemType Directory -Force $helperPublish | Out-Null
  $helperPublishLog=Join-Path $logRoot ("build-publish-updater-helper-"+$stamp+".log")
  Require-Stage 'Updater helper win-x64 self-contained publish' @(
    'publish','.\src\MhwModManager.Updater.Helper\MhwModManager.Updater.Helper.csproj',
    '-c','Release','-r','win-x64','--self-contained','true',
    '-p:PublishSingleFile=false','-p:PublishTrimmed=false','-p:PublishReadyToRun=false',
    '-p:DebugType=None',
    '-o',$helperPublish) $helperPublishLog
  $helperExe=Join-Path $helperPublish 'MHW Mod Manager Updater.exe'
  if(-not (Test-Path -LiteralPath $helperExe -PathType Leaf)){throw "Updater helper publish did not produce $helperExe"}
  $helperFiles=@(Get-ChildItem -LiteralPath $helperPublish -Recurse -File)
  if($helperFiles.Count -lt 2){throw "Updater helper invocation closure is unexpectedly incomplete."}
  Write-MhwMasterDebug -Root $Root -Area 'BUILD-UPDATER' -Message ("HelperClosureFiles="+$helperFiles.Count)

  Write-MhwMasterDebug -Root $Root -Area 'BUILD-PACKAGE' -Message ('Copying documentation to '+$publish)
  Copy-Item .\README.md,.\CHANGELOG.md,.\VERSION.txt,.\VALIDATION.md -Destination $publish
  Write-MhwMasterDebug -Root $Root -Area 'BUILD-PACKAGE' -Message 'Copying docs directory.'
  Copy-Item .\docs -Destination $publish -Recurse
  Write-MhwMasterDebug -Root $Root -Area 'BUILD-PACKAGE' -Message 'Copying scripts directory.'
  Copy-Item .\scripts -Destination $publish -Recurse
  Write-MhwMasterDebug -Root $Root -Area 'BUILD-PACKAGE' -Message 'Copying legacy-v7 directory.'
  Copy-Item .\legacy-v7 -Destination $publish -Recurse
  if(Test-Path '.\Open Startup Logs.bat'){Copy-Item '.\Open Startup Logs.bat' -Destination $publish -Force}
  if(Test-Path '.\OPEN MASTER DEBUG LOG.bat'){Copy-Item '.\OPEN MASTER DEBUG LOG.bat' -Destination $publish -Force}
  if(Test-Path '.\RUN BUILT APP.bat'){Write-MhwMasterDebug -Root $Root -Area 'BUILD-PACKAGE' -Message 'Root RUN BUILT APP.bat retained at project root for unified source-state launch.'}
  Write-MhwMasterDebug -Root $Root -Area 'BUILD-PACKAGE' -Message 'Copying root helpers and armor database; runtime debug logs remain CI/local evidence only.'
  New-Item -ItemType Directory -Force (Join-Path $publish 'data')|Out-Null
  Copy-Item '.\data\Armor Database.csv' (Join-Path $publish 'data\Armor Database.csv') -Force
  @(
    'KEEP YOUR EXISTING Mods FOLDER.',
    'KEEP YOUR ENTIRE EXISTING State FOLDER.',
    "v$appVersion writes its database/blobs under State\Next and does not replace State\V2.",
    'Do not remove legacy-v7 until migration, one deployment, Health, and restart recovery all pass.'
  )|Set-Content (Join-Path $publish 'KEEP MODS AND STATE.txt') -Encoding UTF8

  Write-MhwMasterDebug -Root $Root -Area 'BUILD-PACKAGE' -Message 'Writing KEEP MODS AND STATE instructions.'

  $buildIdentity=[ordered]@{
    schemaVersion=1
    channel=$updateChannel
    productVersion=$appVersion
    sourceSha=$sourceSha
    buildNumber=$buildNumber
    builtUtc=$buildIdentityUtc
  }
  $buildIdentityPath=Join-Path $publish 'build-identity.json'
  $buildIdentity | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $buildIdentityPath -Encoding UTF8

  $protectedRoots=@('Mods','State','Inbox','Mods Archive','Games','Support Bundles','BuildLogs')
  $productEntries=New-Object System.Collections.Generic.List[object]
  foreach($file in Get-ChildItem -LiteralPath $publish -Recurse -File | Sort-Object FullName){
    $relative=$file.FullName.Substring($publish.Length).TrimStart('\','/').Replace('\','/')
    if($relative -ieq 'product-files.json' -or $relative -ieq 'release-install.json'){continue}
    $first=$relative.Split('/')[0]
    if($protectedRoots -contains $first){throw "Release payload unexpectedly contains protected user/runtime root: $relative"}
    if($relative -ieq 'MHW-DEBUG-ALL.log'){throw 'Release payload unexpectedly contains mutable MHW-DEBUG-ALL.log.'}
    $productEntries.Add([pscustomobject][ordered]@{
      path=$relative
      size=[long]$file.Length
      sha256=(Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
    })
  }
  $productManifest=[ordered]@{schemaVersion=1;files=$productEntries.ToArray()}
  $productManifestPath=Join-Path $publish 'product-files.json'
  $productManifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $productManifestPath -Encoding UTF8
  $productManifestHash=(Get-FileHash -LiteralPath $productManifestPath -Algorithm SHA256).Hash

  $releaseInstall=[ordered]@{
    schemaVersion=1
    productId='fengie/mhw-mods:MHW-Manual-Mod-Manager'
    channel=$updateChannel
    build=$buildIdentity
    executableRelativePath=$executableRelativePath
    productManifestSha256=$productManifestHash
  }
  $releaseInstallPath=Join-Path $publish 'release-install.json'
  $releaseInstall | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $releaseInstallPath -Encoding UTF8
  Write-MhwMasterDebug -Root $Root -Area 'BUILD-UPDATER' -Message ("ProductManifestSha256="+$productManifestHash+"; OwnedFiles="+$productEntries.Count)

  $art=Join-Path $Root 'artifacts';New-Item -ItemType Directory -Force $art|Out-Null
  $zip=Join-Path $art ($releaseName+'-win-x64.zip')
  Remove-Item $zip -Force -ErrorAction SilentlyContinue
  Write-MhwMasterDebug -Root $Root -Area 'BUILD-PACKAGE' -Message ('Compressing deterministic release artifact: '+$zip)
  New-DeterministicZip -SourceRoot $publish -Destination $zip -Timestamp $commitTime
  Write-MhwMasterDebug -Root $Root -Area 'BUILD-PACKAGE' -Message 'Computing SHA256.'
  $hash=(Get-FileHash $zip -Algorithm SHA256).Hash
  $zipInfo=Get-Item -LiteralPath $zip
  $updateManifestPath=Join-Path $art 'update-manifest.json'
  $updateManifest=[ordered]@{
    schemaVersion=1
    channel=$updateChannel
    productVersion=$appVersion
    sourceSha=$sourceSha
    buildNumber=$buildNumber
    artifactName=$zipInfo.Name
    artifactSize=[long]$zipInfo.Length
    sha256=$hash
    productManifestSha256=$productManifestHash
    executableRelativePath=$executableRelativePath
    minimumUpdaterProtocol=1
    publishedUtc=$buildIdentityUtc
  }
  $updateManifestJson=$updateManifest | ConvertTo-Json -Depth 8
  [IO.File]::WriteAllText($updateManifestPath,$updateManifestJson,[Text.UTF8Encoding]::new($false))

  & (Join-Path $PSScriptRoot 'Test-UpdaterPackage.ps1') -ArtifactPath $zip -ManifestPath $updateManifestPath -ExpectedSourceSha $sourceSha -ExpectedBuildNumber $buildNumber

  # Verification promotion belongs after every release-producing check. If updater
  # metadata, compression, or final package verification fails, preserve the
  # previous verified cache rather than promoting a release that did not finish.
  Require-Stage 'Promote verified function fingerprints' @('run','-c','Release','--project','.\tools\MhwModManager.FunctionVerifier\MhwModManager.FunctionVerifier.csproj','--no-restore','--no-build','--','--root',$Root,'--mode','confirm','--baseline',$functionBaseline,'--trusted-files',$trustedFunctionFiles,'--trusted-source',$trustedFunctionSource,'--report',$functionConfirmReport) (Join-Path $logRoot ("build-function-confirm-"+$stamp+".log"))

  @(
    ('Version: '+$appVersion),
    ('Source SHA: '+$sourceSha),
    ('Updater build number: '+$buildNumber),
    ('SDK: '+$version),
    ('ReadyToRun fallback used: '+$usedFallback),
    ('Release folder: '+$publish),
    ('Artifact: '+$zip),
    ('SHA256: '+$hash),
    ('Product manifest SHA256: '+$productManifestHash),
    ('Update manifest: '+$updateManifestPath),
    ('Master transcript: '+$masterLog)
  )|Set-Content $reportPath -Encoding UTF8
  Write-Host "PASS: $zip" -ForegroundColor Green
  Write-Host "SHA256: $hash" -ForegroundColor Green
  Write-Host "Updater build: $buildNumber / $sourceSha" -ForegroundColor Green
  Write-Host "Update manifest: $updateManifestPath" -ForegroundColor Green
  Write-Host "Build report: $reportPath" -ForegroundColor Green
  Write-MhwMasterDebug -Root $Root -Area 'BUILD' -Message ("Artifact="+$zip+"; SHA256="+$hash+"; Report="+$reportPath)
  $buildSucceeded=$true
} catch {
  Write-MhwMasterDebug -Root $Root -Area 'BUILD-FATAL' -Message $_.Exception.ToString()
  throw
} finally {
  Pop-Location
  if($transcriptStarted){try{Stop-Transcript|Out-Null}catch{}}
  Stop-MhwMasterDebugSession -Root $Root -Area 'BUILD' -Summary ($(if($buildSucceeded){'PASS'}else{'FAIL'}))
}
