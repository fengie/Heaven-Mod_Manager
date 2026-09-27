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
if($env:OS -ne 'Windows_NT'){throw 'The production WPF release must be built and integration-tested on Windows.'}

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

Push-Location $Root
try{
  Write-Host ("SDK: "+$version)
  Write-Host ("Master build transcript: "+$masterLog)
  Write-Host ("MASTER DEBUG LOG (send this file): "+$MasterDebug) -ForegroundColor Yellow
  Write-MhwMasterDebug -Root $Root -Area 'BUILD' -Message ("SDK="+$version+"; Transcript="+$masterLog+"; MasterDebug="+$MasterDebug)

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
  Require-Stage 'App win-x64 compile/analyzers' @('build','.\src\MhwModManager.App\MhwModManager.App.csproj','-c','Release','-r','win-x64','--self-contained','true','-warnaserror') $ridCompileLog

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
      ('ReadyToRun log: '+$publishR2RLog)
    ) | Set-Content (Join-Path $publish 'PUBLISH FALLBACK.txt') -Encoding UTF8
  }

  # Promote function fingerprints only after the complete Windows build/test/publish path has succeeded.
  # Any exception before this point preserves the previously verified=true cache.
  Require-Stage 'Promote verified function fingerprints' @('run','-c','Release','--project','.\tools\MhwModManager.FunctionVerifier\MhwModManager.FunctionVerifier.csproj','--no-restore','--no-build','--','--root',$Root,'--mode','confirm','--baseline',$functionBaseline,'--trusted-files',$trustedFunctionFiles,'--trusted-source',$trustedFunctionSource,'--report',$functionConfirmReport) (Join-Path $logRoot ("build-function-confirm-"+$stamp+".log"))

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
  if(Test-Path $MasterDebug){Copy-Item $MasterDebug (Join-Path $publish 'MHW-DEBUG-ALL.log') -Force}
  Write-MhwMasterDebug -Root $Root -Area 'BUILD-PACKAGE' -Message 'Copying root helpers, master debug log, and armor database.'
  New-Item -ItemType Directory -Force (Join-Path $publish 'data')|Out-Null
  Copy-Item '.\data\Armor Database.csv' (Join-Path $publish 'data\Armor Database.csv') -Force
  @(
    'KEEP YOUR EXISTING Mods FOLDER.',
    'KEEP YOUR ENTIRE EXISTING State FOLDER.',
    "v$appVersion writes its database/blobs under State\Next and does not replace State\V2.",
    'Do not remove legacy-v7 until migration, one deployment, Health, and restart recovery all pass.'
  )|Set-Content (Join-Path $publish 'KEEP MODS AND STATE.txt') -Encoding UTF8

  Write-MhwMasterDebug -Root $Root -Area 'BUILD-PACKAGE' -Message 'Writing KEEP MODS AND STATE instructions.'
  $art=Join-Path $Root 'artifacts';New-Item -ItemType Directory -Force $art|Out-Null
  $zip=Join-Path $art ($releaseName+'-win-x64.zip')
  Remove-Item $zip -Force -ErrorAction SilentlyContinue
  Write-MhwMasterDebug -Root $Root -Area 'BUILD-PACKAGE' -Message ('Compressing release artifact: '+$zip)
  Compress-Archive -Path (Join-Path $publish '*') -DestinationPath $zip -CompressionLevel Optimal
  Write-MhwMasterDebug -Root $Root -Area 'BUILD-PACKAGE' -Message 'Computing SHA256.'
  $hash=(Get-FileHash $zip -Algorithm SHA256).Hash
  @(
    ('Version: '+$appVersion),
    ('SDK: '+$version),
    ('ReadyToRun fallback used: '+$usedFallback),
    ('Release folder: '+$publish),
    ('Artifact: '+$zip),
    ('SHA256: '+$hash),
    ('Master transcript: '+$masterLog)
  )|Set-Content $reportPath -Encoding UTF8
  Write-Host "PASS: $zip" -ForegroundColor Green
  Write-Host "SHA256: $hash" -ForegroundColor Green
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
  if($publishForMasterCopy -and (Test-Path $publishForMasterCopy) -and (Test-Path $MasterDebug)){
    try{Copy-Item $MasterDebug (Join-Path $publishForMasterCopy 'MHW-DEBUG-ALL.log') -Force}catch{}
  }
}
