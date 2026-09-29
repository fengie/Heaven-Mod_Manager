param([switch]$RunBenchmarks)
$ErrorActionPreference='Stop'
$Root=(Resolve-Path (Join-Path $PSScriptRoot '..')).Path
. (Join-Path $PSScriptRoot 'Master-Debug.ps1')
$MasterDebug=Get-MhwMasterDebugPath -Root $Root
Start-MhwMasterDebugSession -Root $Root -Area 'VERIFY' -Title 'Full verification run'
$dotnet=(Get-Command dotnet -ErrorAction Stop).Source
$sdk=& $dotnet --version
if([version]$sdk -lt [version]'10.0.401'){throw "Install .NET SDK 10.0.401 or newer. Found $sdk"}

$BuildLogs=Join-Path $Root 'BuildLogs'
New-Item -ItemType Directory -Force -Path $BuildLogs | Out-Null
$Stamp=Get-Date -Format 'yyyyMMdd-HHmmss'
$TranscriptPath=Join-Path $BuildLogs "verify-$Stamp.log"
$MarkdownReport=Join-Path $BuildLogs "verification-report-$Stamp.md"
$JsonReport=Join-Path $BuildLogs "verification-report-$Stamp.json"
$RelaxedBinlog=Join-Path $BuildLogs "relaxed-all-$Stamp.binlog"
$FinalBinlog=Join-Path $BuildLogs "build-$Stamp.binlog"
$FunctionReport=Join-Path $BuildLogs "function-verification-$Stamp.json"
$FunctionConfirmReport=Join-Path $BuildLogs "function-verification-confirm-$Stamp.json"
$FunctionBaseline=Join-Path $Root ".verification\function-status.json"
$TrustedFunctionFiles=Join-Path $Root ".verification\trusted-v8.7.0-files.json"
$TrustedFunctionSource=Join-Path $Root ".verification\trusted-v8.7.0-src.zip"
$StageStatusPath=Join-Path $Root ".verification\stage-status.json"
$script:StageCache=@{}
$TranscriptStarted=$false
$Pushed=$false
$script:Results=@()

$Projects=@(
    @{Name='Core';Path='.\src\MhwModManager.Core\MhwModManager.Core.csproj'},
    @{Name='Storage';Path='.\src\MhwModManager.Storage\MhwModManager.Storage.csproj'},
    @{Name='Filesystem';Path='.\src\MhwModManager.Filesystem\MhwModManager.Filesystem.csproj'},
    @{Name='Mhw';Path='.\src\MhwModManager.Mhw\MhwModManager.Mhw.csproj'},
    @{Name='Diagnostics';Path='.\src\MhwModManager.Diagnostics\MhwModManager.Diagnostics.csproj'},
    @{Name='Automation';Path='.\src\MhwModManager.Automation\MhwModManager.Automation.csproj'},
    @{Name='UnitTests';Path='.\tests\MhwModManager.Tests\MhwModManager.Tests.csproj'},
    @{Name='AutomationTests';Path='.\tests\MhwModManager.AutomationTests\MhwModManager.AutomationTests.csproj'},
    @{Name='IntegrationTests';Path='.\tests\MhwModManager.IntegrationTests\MhwModManager.IntegrationTests.csproj'},
    @{Name='Benchmarks';Path='.\benchmarks\MhwModManager.Benchmarks\MhwModManager.Benchmarks.csproj'},
    @{Name='SelfTest';Path='.\tools\MhwModManager.SelfTest\MhwModManager.SelfTest.csproj'},
    @{Name='FunctionVerifier';Path='.\tools\MhwModManager.FunctionVerifier\MhwModManager.FunctionVerifier.csproj'},
    @{Name='App';Path='.\src\MhwModManager.App\MhwModManager.App.csproj'}
)

function Initialize-StageCache {
    $script:StageCache=@{}
    if(!(Test-Path $StageStatusPath)){return}
    try {
        $doc=Get-Content -Raw -Path $StageStatusPath | ConvertFrom-Json
        if([int]$doc.formatVersion -ne 1){throw "Unsupported stage cache format $($doc.formatVersion)."}
        foreach($entry in @($doc.stages)){
            if($null -eq $entry -or [string]::IsNullOrWhiteSpace([string]$entry.id)){continue}
            $script:StageCache[[string]$entry.id]=$entry
        }
    }
    catch {
        throw "Verification stage cache is invalid: $StageStatusPath`n$($_.Exception.Message)"
    }
}

function Save-StageCache {
    $directory=Split-Path -Parent $StageStatusPath
    if(!(Test-Path $directory)){New-Item -ItemType Directory -Force -Path $directory | Out-Null}
    $entries=@($script:StageCache.Values | Sort-Object { [string]$_.id })
    $payload=[ordered]@{
        formatVersion=1
        sourceVersion=(Get-Content -Raw -Path (Join-Path $Root 'VERSION.txt')).Trim()
        updatedAtUtc=[DateTimeOffset]::UtcNow.ToString('o')
        stages=$entries
    }
    $temp=$StageStatusPath+'.tmp-'+[Guid]::NewGuid().ToString('N')
    try {
        $payload | ConvertTo-Json -Depth 8 | Set-Content -Path $temp -Encoding UTF8
        Move-Item -LiteralPath $temp -Destination $StageStatusPath -Force
    }
    finally { if(Test-Path $temp){Remove-Item -LiteralPath $temp -Force -ErrorAction SilentlyContinue} }
}

function Add-ProjectInputs([string]$ProjectPath,[System.Collections.Generic.HashSet[string]]$Visited,[System.Collections.Generic.HashSet[string]]$Files){
    $candidate=$ProjectPath
    if(![IO.Path]::IsPathRooted($candidate)){$candidate=Join-Path $Root $candidate}
    $full=[IO.Path]::GetFullPath($candidate)
    if(!(Test-Path $full)){throw "Project input not found: $full"}
    if(!$Visited.Add($full)){return}
    [void]$Files.Add($full)
    $projectDir=Split-Path -Parent $full
    Get-ChildItem -LiteralPath $projectDir -Recurse -File | Where-Object {
        $rel=$_.FullName.Substring($projectDir.Length).TrimStart([char[]]@('\','/'))
        $parts=$rel -split '[\\/]'
        -not ($parts -contains 'bin') -and -not ($parts -contains 'obj')
    } | ForEach-Object { [void]$Files.Add($_.FullName) }
    [xml]$xml=Get-Content -Raw -LiteralPath $full
    foreach($node in @($xml.SelectNodes("//*[local-name()='ProjectReference']"))){
        $include=[string]$node.Include
        if([string]::IsNullOrWhiteSpace($include)){continue}
        Add-ProjectInputs (Join-Path $projectDir $include) $Visited $Files
    }
}

function Get-ProjectFingerprint([string]$ProjectPath){
    $visited=New-Object 'System.Collections.Generic.HashSet[string]' -ArgumentList ([StringComparer]::OrdinalIgnoreCase)
    $files=New-Object 'System.Collections.Generic.HashSet[string]' -ArgumentList ([StringComparer]::OrdinalIgnoreCase)
    Add-ProjectInputs $ProjectPath $visited $files
    foreach($common in @('Directory.Build.props','Directory.Build.targets','Directory.Packages.props','global.json','.editorconfig','NuGet.Config','nuget.config')){
        $path=Join-Path $Root $common
        if(Test-Path $path){[void]$files.Add([IO.Path]::GetFullPath($path))}
    }
    # Integration tests inspect App/XAML, verification tooling, and handoff documents
    # at runtime without ProjectReference edges to those inputs. Include them too.
    if([IO.Path]::GetFileName($ProjectPath.Replace([char]92,[char]47)) -eq 'MhwModManager.IntegrationTests.csproj'){
        foreach($folder in @('src','tools/MhwModManager.FunctionVerifier','scripts')){
            Get-ChildItem -LiteralPath (Join-Path $Root $folder) -Recurse -File -Force | Where-Object {
                $parts=$_.FullName.Substring($Root.Length) -split '[\\/]'
                -not ($parts -contains 'bin') -and -not ($parts -contains 'obj')
            } | ForEach-Object {[void]$files.Add($_.FullName)}
        }
        foreach($relative in @('NEXT-AGENT-START-HERE.md','_AGENT_CONTEXT/CONTINUITY_PROTOCOL.md','_AGENT_CONTEXT/handoff-manifest.json')){
            [void]$files.Add((Join-Path $Root $relative))
        }
    }
    $builder=New-Object Text.StringBuilder
    [void]$builder.Append("toolchain|dotnet=").Append([string]$sdk).Append("|os=").Append([string]$env:OS).Append("|arch=").Append([string]$env:PROCESSOR_ARCHITECTURE).Append("`n")
    foreach($file in @($files | Sort-Object { $_.ToLowerInvariant() })){
        $rootPrefix=[IO.Path]::GetFullPath($Root).TrimEnd([char[]]@('\','/'))+[IO.Path]::DirectorySeparatorChar
        $fullFile=[IO.Path]::GetFullPath($file)
        if(!$fullFile.StartsWith($rootPrefix,[StringComparison]::OrdinalIgnoreCase)){throw "Verification input escaped repository root: $fullFile"}
        $relative=$fullFile.Substring($rootPrefix.Length).Replace('\','/')
        $hash=(Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant()
        [void]$builder.Append($relative).Append('|').Append($hash).Append("`n")
    }
    $sha=[Security.Cryptography.SHA256]::Create()
    try {
        $bytes=[Text.Encoding]::UTF8.GetBytes($builder.ToString())
        return ([BitConverter]::ToString($sha.ComputeHash($bytes))).Replace('-','').ToLowerInvariant()
    }
    finally {$sha.Dispose()}
}

function Get-CachedStage([string]$Id,[string]$Fingerprint){
    if(!$script:StageCache.ContainsKey($Id)){return $null}
    $entry=$script:StageCache[$Id]
    if($entry.verified -eq $true -and [string]$entry.fingerprint -eq $Fingerprint){return $entry}
    return $null
}

function Set-CachedStage([string]$Id,[string]$Fingerprint,[string]$Group,[string]$Name,[string]$Evidence){
    $script:StageCache[$Id]=[pscustomobject]@{
        id=$Id
        fingerprint=$Fingerprint
        verified=$true
        verifiedAtUtc=[DateTimeOffset]::UtcNow.ToString('o')
        group=$Group
        name=$Name
        evidence=$Evidence
    }
    Save-StageCache
}

function Add-Result([string]$Group,[string]$Name,[bool]$Passed,[int]$ExitCode,[double]$Seconds,[string]$Log,[string]$Detail){
    $script:Results += [pscustomobject]@{group=$Group;name=$Name;passed=$Passed;exitCode=$ExitCode;seconds=[math]::Round($Seconds,3);log=$Log;detail=$Detail}
}

function Invoke-DotnetStep([string]$Group,[string]$Name,[string[]]$Arguments,[string]$LogName){
    $log=Join-Path $BuildLogs ("{0}-{1}-{2}.log" -f $LogName,$Stamp,($Name -replace '[^A-Za-z0-9_.-]','_'))
    Write-Host "`n=== $Group :: $Name ===" -ForegroundColor Cyan
    Write-MhwMasterDebug -Root $Root -Area 'VERIFY-STAGE' -Message ("START: "+$Group+" :: "+$Name+" | DetailLog="+$log)
    $sw=[System.Diagnostics.Stopwatch]::StartNew()
    $exit=9998
    $previousPreference=$ErrorActionPreference
    try {
        # Native tools may write ordinary diagnostic text to stderr. Windows PowerShell 5.1
        # converts redirected stderr into ErrorRecord objects; with Stop that would abort the
        # verifier instead of recording the stage failure and moving on.
        $ErrorActionPreference='Continue'
        & $dotnet @Arguments 2>&1 | ForEach-Object { $_.ToString() } | Tee-Object -FilePath $log | ForEach-Object {
            $line=$_.ToString()
            Write-Host $line
            Write-MhwMasterDebug -Root $Root -Area 'DOTNET' -Message ("["+$Group+" :: "+$Name+"] "+$line)
        }
        $exit=$LASTEXITCODE
    }
    catch {
        $_.Exception.ToString() | Add-Content -Path $log -Encoding UTF8
        Write-Host $_.Exception.ToString() -ForegroundColor Red
        Write-MhwMasterDebug -Root $Root -Area 'VERIFY-EXCEPTION' -Message $_.Exception.ToString()
        if($LASTEXITCODE -is [int]){$exit=$LASTEXITCODE}
    }
    finally {
        $ErrorActionPreference=$previousPreference
        $sw.Stop()
    }
    $ok=($exit -eq 0)
    if($ok){
        Write-Host "PASS: $Name" -ForegroundColor Green
        Write-MhwMasterDebug -Root $Root -Area 'VERIFY-STAGE' -Message ("PASS: "+$Group+" :: "+$Name+" | ExitCode="+$exit+" | Seconds="+$sw.Elapsed.TotalSeconds)
        $detail='ok'
    } else {
        Write-Host "FAIL: $Name (exit $exit)" -ForegroundColor Red
        Write-MhwMasterDebug -Root $Root -Area 'VERIFY-STAGE' -Message ("FAIL: "+$Group+" :: "+$Name+" | ExitCode="+$exit+" | DetailLog="+$log)
        $diagnostics=@()
        if(Test-Path $log){
            $diagnostics=@(Get-Content $log | Where-Object { $_ -match '(?i)(: error |: warning (CA|CS|xUnit|IDE)[0-9]+|Failed!|\bFailed\b|Error Message:|Unhandled exception|Exception:)' } | Select-Object -First 24)
        }
        if($diagnostics.Count -gt 0){
            Write-Host '  First diagnostics:' -ForegroundColor Yellow
            foreach($line in $diagnostics){Write-Host ("  " + [string]$line) -ForegroundColor Yellow;Write-MhwMasterDebug -Root $Root -Area 'VERIFY-DIAGNOSTIC' -Message ("["+$Group+" :: "+$Name+"] "+[string]$line)}
            $detail=$diagnostics[0].ToString()
        } else {
            Write-Host "  No compact diagnostic matched; see $log" -ForegroundColor Yellow
            $detail='See per-stage log for full compiler/test output.'
        }
    }
    Add-Result $Group $Name $ok $exit $sw.Elapsed.TotalSeconds $log $detail
    return $ok
}


function Invoke-CachedDotnetStep([string]$CacheId,[string]$ProjectPath,[string]$Group,[string]$Name,[string[]]$Arguments,[string]$LogName){
    $fingerprint=Get-ProjectFingerprint $ProjectPath
    $cached=Get-CachedStage $CacheId $fingerprint
    if($null -ne $cached){
        $detail="cached verified=true; exact project/dependency fingerprint unchanged; originally verified $($cached.verifiedAtUtc); check skipped"
        Write-Host "`n=== $Group :: $Name ===" -ForegroundColor Cyan
        Write-Host "PASS-CACHED: $Name" -ForegroundColor Green
        Write-MhwMasterDebug -Root $Root -Area 'VERIFY-CACHE' -Message ("HIT: "+$CacheId+" | fingerprint="+$fingerprint+" | "+$detail)
        Add-Result $Group $Name $true 0 0 '' $detail
        return $true
    }
    $ok=Invoke-DotnetStep $Group $Name $Arguments $LogName
    if($ok){
        Set-CachedStage $CacheId $fingerprint $Group $Name ("Verified by current run with exit code 0.")
        Write-MhwMasterDebug -Root $Root -Area 'VERIFY-CACHE' -Message ("CHECKED: "+$CacheId+" | verified=true | fingerprint="+$fingerprint)
    }
    return $ok
}


function Invoke-PowerShellSyntaxSweep {
    Write-MhwMasterDebug -Root $Root -Area 'VERIFY-HARNESS' -Message 'START PowerShell syntax sweep'
    $log=Join-Path $BuildLogs ("powershell-syntax-{0}.log" -f $Stamp)
    $targets=@(Get-ChildItem (Join-Path $Root 'scripts') -Filter '*.ps1' -File | Sort-Object FullName)
    $allErrors=New-Object System.Collections.Generic.List[string]
    foreach($target in $targets){
        Write-MhwMasterDebug -Root $Root -Area 'VERIFY-HARNESS' -Message ("Parsing PowerShell: "+$target.FullName)
        $tokens=$null
        $parseErrors=$null
        [System.Management.Automation.Language.Parser]::ParseFile($target.FullName,[ref]$tokens,[ref]$parseErrors) | Out-Null
        if($parseErrors -and $parseErrors.Count -gt 0){
            foreach($parseError in $parseErrors){
                $line=$parseError.Extent.StartLineNumber
                $column=$parseError.Extent.StartColumnNumber
                $allErrors.Add(("{0}:{1}:{2}: {3}" -f $target.FullName,$line,$column,$parseError.Message))
            }
        }
    }
    if($allErrors.Count -gt 0){
        $allErrors | Set-Content -Path $log -Encoding UTF8
        Write-Host "FAIL: PowerShell syntax sweep ($($allErrors.Count) parse error(s))" -ForegroundColor Red
        foreach($line in $allErrors){Write-Host $line -ForegroundColor Red}
        Add-Result 'Harness' 'PowerShell syntax sweep' $false 3 0 $log "$($allErrors.Count) parse error(s) found in active scripts."
        foreach($line in $allErrors){Write-MhwMasterDebug -Root $Root -Area 'VERIFY-HARNESS' -Message ("PowerShell parse error: "+$line)}
        return $false
    }
    'All active PowerShell scripts parsed successfully.' | Set-Content -Path $log -Encoding UTF8
    Write-Host 'PASS: PowerShell syntax sweep' -ForegroundColor Green
    Write-MhwMasterDebug -Root $Root -Area 'VERIFY-HARNESS' -Message 'PASS PowerShell syntax sweep'
    Add-Result 'Harness' 'PowerShell syntax sweep' $true 0 0 $log 'All active PowerShell scripts parsed successfully.'
    return $true
}

function Invoke-HarnessReportPreflight {
    Write-MhwMasterDebug -Root $Root -Area 'VERIFY-HARNESS' -Message 'START report serialization preflight'
    $log=Join-Path $BuildLogs ("harness-report-preflight-{0}.log" -f $Stamp)
    $sw=[System.Diagnostics.Stopwatch]::StartNew()
    try {
        $sample=@(
            [pscustomobject]@{group='Harness';name='sample pass';passed=$true;exitCode=0;seconds=0.1;log='';detail='ok'},
            [pscustomobject]@{group='Harness';name='sample fail';passed=$false;exitCode=2;seconds=0.2;log='sample.log';detail='sample'}
        )
        $payload=[ordered]@{generatedAt=(Get-Date).ToString('o');sdk=[string]$sdk;overall='TEST';passed=1;failed=1;results=@($sample)}
        $json=$payload | ConvertTo-Json -Depth 10
        if([string]::IsNullOrWhiteSpace($json)){throw 'ConvertTo-Json returned empty output.'}
        $json | Set-Content -Path $log -Encoding UTF8
        $null=Get-Content -Raw -Path $log | ConvertFrom-Json
        $sw.Stop()
        Add-Result 'Harness' 'Report serialization preflight' $true 0 $sw.Elapsed.TotalSeconds $log 'Plain PowerShell object-array report serialization round-tripped successfully.'
        Write-Host 'PASS: Report serialization preflight' -ForegroundColor Green
        Write-MhwMasterDebug -Root $Root -Area 'VERIFY-HARNESS' -Message 'PASS report serialization preflight'
        return $true
    }
    catch {
        $sw.Stop()
        $_.Exception.ToString() | Set-Content -Path $log -Encoding UTF8
        Add-Result 'Harness' 'Report serialization preflight' $false 4 $sw.Elapsed.TotalSeconds $log $_.Exception.Message
        Write-Host "FAIL: Report serialization preflight - $($_.Exception.Message)" -ForegroundColor Red
        Write-MhwMasterDebug -Root $Root -Area 'VERIFY-HARNESS' -Message $_.Exception.ToString()
        return $false
    }
}

function Invoke-AgentHandoffPreflight {
    Write-MhwMasterDebug -Root $Root -Area 'VERIFY-HARNESS' -Message 'START agent handoff continuity preflight'
    $log=Join-Path $BuildLogs ("agent-handoff-preflight-{0}.log" -f $Stamp)
    $sw=[System.Diagnostics.Stopwatch]::StartNew()
    try {
        & (Join-Path $PSScriptRoot 'Test-AgentHandoff.ps1') -Root $Root *>&1 | ForEach-Object { $_.ToString() } | Tee-Object -FilePath $log | Out-Host
        & (Join-Path $PSScriptRoot 'Test-AgentHandoff-NegativeFixtures.ps1') -Root $Root *>&1 | ForEach-Object { $_.ToString() } | Tee-Object -FilePath $log -Append | Out-Host
        & (Join-Path $PSScriptRoot 'Test-VerificationCache.ps1') *>&1 | ForEach-Object { $_.ToString() } | Tee-Object -FilePath $log -Append | Out-Host
        $sw.Stop()
        Add-Result 'Harness' 'Agent handoff continuity preflight' $true 0 $sw.Elapsed.TotalSeconds $log 'Required continuity context/manifest is present and propagates to the next agent.'
        Write-Host 'PASS: Agent handoff continuity preflight' -ForegroundColor Green
        Write-MhwMasterDebug -Root $Root -Area 'VERIFY-HARNESS' -Message 'PASS agent handoff continuity preflight'
        return $true
    }
    catch {
        $sw.Stop()
        $_.Exception.ToString() | Set-Content -Path $log -Encoding UTF8
        Add-Result 'Harness' 'Agent handoff continuity preflight' $false 5 $sw.Elapsed.TotalSeconds $log $_.Exception.Message
        Write-Host "FAIL: Agent handoff continuity preflight - $($_.Exception.Message)" -ForegroundColor Red
        Write-MhwMasterDebug -Root $Root -Area 'VERIFY-HARNESS' -Message $_.Exception.ToString()
        return $false
    }
}

function Write-Reports {
    Write-MhwMasterDebug -Root $Root -Area 'VERIFY-REPORT' -Message ("Writing reports: Markdown=$MarkdownReport; JSON=$JsonReport")
    $resultArray=@($script:Results)
    $passed=@($resultArray | Where-Object { $_.passed -eq $true }).Count
    $failed=@($resultArray | Where-Object { $_.passed -ne $true }).Count
    $overall=if($failed -eq 0){'PASS'}else{'FAIL'}
    $lines=New-Object System.Collections.Generic.List[string]
    $lines.Add('# MHW Manual Mod Manager verification report')
    $lines.Add('')
    $lines.Add("Generated: $(Get-Date -Format o)")
    $lines.Add("SDK: $sdk")
    $lines.Add("Overall: **$overall** - $passed passed / $failed failed")
    $lines.Add('')
    $lines.Add('| Group | Stage | Result | Exit | Seconds | Detail | Log |')
    $lines.Add('|---|---|---:|---:|---:|---|---|')
    foreach($r in $resultArray){
        $detail=([string]$r.detail).Replace('|','\|').Replace("`r",' ').Replace("`n",' ')
        $resultText=if($r.passed){'PASS'}else{'FAIL'}
        $lines.Add("| $($r.group) | $($r.name) | $resultText | $($r.exitCode) | $($r.seconds) | $detail | $($r.log) |")
    }
    $lines.Add('')
    if($failed -gt 0){
        $lines.Add('## Failures')
        $lines.Add('')
        foreach($r in $resultArray | Where-Object { $_.passed -ne $true }){
            $lines.Add("### $($r.group) / $($r.name)")
            $lines.Add('')
            $lines.Add("Exit code: $($r.exitCode)  ")
            $lines.Add("Full output: $($r.log)")
            if($r.log -and (Test-Path $r.log)){
                $excerpt=@(Get-Content $r.log | Where-Object {$_ -match '(?i)(error|failed|exception|assert|warning [A-Z]+[0-9]+|CS[0-9]{4}|CA[0-9]{4}|xUnit[0-9]{4})'} | Select-Object -First 120)
                if($excerpt.Count -gt 0){
                    $lines.Add('')
                    $lines.Add('```text')
                    foreach($line in $excerpt){$lines.Add([string]$line)}
                    $lines.Add('```')
                }
            }
            $lines.Add('')
        }
        $lines.Add('The verifier intentionally continued after each failure so later subsystems were still exercised.')
    }
    $lines | Set-Content -Path $MarkdownReport -Encoding UTF8

    # Keep the report payload as a plain PowerShell object array. Windows PowerShell 5.1
    # has binder edge cases around generic List[object] values in custom/JSON objects.
    $jsonObject=[ordered]@{
        generatedAt=(Get-Date).ToString('o')
        sdk=[string]$sdk
        overall=$overall
        passed=$passed
        failed=$failed
        results=$resultArray
    }
    try {
        $jsonObject | ConvertTo-Json -Depth 10 | Set-Content -Path $JsonReport -Encoding UTF8
    }
    catch {
        $fallback=Join-Path $BuildLogs ("verification-report-json-error-{0}.txt" -f $Stamp)
        $_.Exception.ToString() | Set-Content -Path $fallback -Encoding UTF8
        Write-Host "JSON report generation failed; Markdown report is still complete. Details: $fallback" -ForegroundColor Yellow
        Write-MhwMasterDebug -Root $Root -Area 'VERIFY-REPORT' -Message ("JSON report generation failed: "+$_.Exception.ToString())
    }
    Write-MhwMasterDebug -Root $Root -Area 'VERIFY-REPORT' -Message ("Report write complete. Overall="+$overall+"; Passed="+$passed+"; Failed="+$failed)
}

try {
    Start-Transcript -Path $TranscriptPath -Force | Out-Null
    $TranscriptStarted=$true
    Push-Location $Root
    $Pushed=$true
    Write-Host "SDK: $sdk" -ForegroundColor Cyan
    Write-Host "MASTER DEBUG LOG (send this file): $MasterDebug" -ForegroundColor Yellow
    Write-MhwMasterDebug -Root $Root -Area 'VERIFY' -Message ("SDK="+$sdk+"; Transcript="+$TranscriptPath+"; MasterDebug="+$MasterDebug)
    Write-Host "Master transcript: $TranscriptPath" -ForegroundColor DarkGray
    Write-Host "Final Markdown report: $MarkdownReport" -ForegroundColor DarkGray
    Write-Host "Final JSON report: $JsonReport" -ForegroundColor DarkGray
    Write-Host "Function verification report: $FunctionReport" -ForegroundColor DarkGray

    Initialize-StageCache

    Invoke-PowerShellSyntaxSweep | Out-Null
    Invoke-HarnessReportPreflight | Out-Null
    Invoke-AgentHandoffPreflight | Out-Null

    Invoke-DotnetStep 'Restore' 'Solution restore' @('restore','.\MhwModManager.sln') 'restore' | Out-Null

    Invoke-DotnetStep 'Verification' 'Function fingerprint scan' @('run','-c','Release','--project','.\tools\MhwModManager.FunctionVerifier\MhwModManager.FunctionVerifier.csproj','--no-restore','--','--root',$Root,'--mode','scan','--baseline',$FunctionBaseline,'--trusted-files',$TrustedFunctionFiles,'--trusted-source',$TrustedFunctionSource,'--report',$FunctionReport) 'function-scan' | Out-Null

    Invoke-DotnetStep 'Compile' 'Relaxed whole solution' @('build','.\MhwModManager.sln','-c','Release','--no-restore','-p:TreatWarningsAsErrors=false',"-bl:$RelaxedBinlog") 'compile-relaxed' | Out-Null

    foreach($Project in $Projects){
        $binlog=Join-Path $BuildLogs ("project-{0}-{1}.binlog" -f ($Project.Name -replace '[^A-Za-z0-9_.-]','_'),$Stamp)
        Invoke-CachedDotnetStep ("strict:"+$Project.Name) $Project.Path 'Strict compile/analyzers' $Project.Name @('build',$Project.Path,'-c','Release','--no-restore','-warnaserror','-p:BuildProjectReferences=false',"-bl:$binlog") 'compile-strict' | Out-Null
    }

    Invoke-DotnetStep 'Compile' 'Strict whole solution' @('build','.\MhwModManager.sln','-c','Release','--no-restore','-warnaserror',"-bl:$FinalBinlog") 'compile-final' | Out-Null

    Invoke-CachedDotnetStep 'test:Core unit tests' '.\tests\MhwModManager.Tests\MhwModManager.Tests.csproj' 'Tests' 'Core unit tests' @('test','.\tests\MhwModManager.Tests\MhwModManager.Tests.csproj','-c','Release','--no-restore','--no-build') 'test' | Out-Null
    Invoke-CachedDotnetStep 'test:Automation unit tests' '.\tests\MhwModManager.AutomationTests\MhwModManager.AutomationTests.csproj' 'Tests' 'Automation unit tests' @('test','.\tests\MhwModManager.AutomationTests\MhwModManager.AutomationTests.csproj','-c','Release','--no-restore','--no-build') 'test' | Out-Null
    $isWindowsHost = [Environment]::OSVersion.Platform -eq [PlatformID]::Win32NT
    if($isWindowsHost){
        Invoke-CachedDotnetStep 'test:Integration + fault injection' '.\tests\MhwModManager.IntegrationTests\MhwModManager.IntegrationTests.csproj' 'Tests' 'Integration + fault injection' @('test','.\tests\MhwModManager.IntegrationTests\MhwModManager.IntegrationTests.csproj','-c','Release','--no-restore','--no-build') 'test' | Out-Null
        Invoke-CachedDotnetStep 'test:Full automation self-test' '.\tools\MhwModManager.SelfTest\MhwModManager.SelfTest.csproj' 'Tests' 'Full automation self-test' @('run','-c','Release','--project','.\tools\MhwModManager.SelfTest\MhwModManager.SelfTest.csproj','--no-restore','--no-build','--',$BuildLogs) 'selftest' | Out-Null
    } else {
        Write-MhwMasterDebug -Root $Root -Area 'VERIFY' -Message 'Windows-only integration/self-test stages skipped because host is not Windows.'
        Add-Result 'Tests' 'Integration + fault injection' $false 9001 0 '' 'Skipped: Windows is required.'
        Add-Result 'Tests' 'Full automation self-test' $false 9001 0 '' 'Skipped: Windows is required.'
    }
    $prePromotionFailures=@($script:Results | Where-Object { $_.passed -ne $true }).Count
    if($prePromotionFailures -eq 0){
        Invoke-DotnetStep 'Verification' 'Promote verified function fingerprints' @('run','-c','Release','--project','.\tools\MhwModManager.FunctionVerifier\MhwModManager.FunctionVerifier.csproj','--no-restore','--no-build','--','--root',$Root,'--mode','confirm','--baseline',$FunctionBaseline,'--trusted-files',$TrustedFunctionFiles,'--trusted-source',$TrustedFunctionSource,'--report',$FunctionConfirmReport) 'function-confirm' | Out-Null
    } else {
        Add-Result 'Verification' 'Function cache promotion safely skipped' $true 0 0 '' ("Preserved previous verified=true function cache because "+$prePromotionFailures+" earlier verification stage(s) failed.")
        Write-MhwMasterDebug -Root $Root -Area 'FUNCTION-VERIFY' -Message ("Cache promotion skipped because "+$prePromotionFailures+" earlier verification stage(s) failed.")
    }
    if($RunBenchmarks){Invoke-DotnetStep 'Performance' 'Benchmarks' @('run','-c','Release','--project','.\benchmarks\MhwModManager.Benchmarks\MhwModManager.Benchmarks.csproj','--no-restore','--','--filter','*') 'benchmark' | Out-Null}
}
catch {
    Add-Result 'Harness' 'Verifier exception' $false 9999 0 $TranscriptPath $_.Exception.ToString()
    Write-Host "Verifier harness exception: $($_.Exception.Message)" -ForegroundColor Red
    Write-MhwMasterDebug -Root $Root -Area 'VERIFY-FATAL' -Message $_.Exception.ToString()
}
finally {
    if($Pushed){Pop-Location}
    try { Write-Reports } catch { Write-Host "Report writer failed: $($_.Exception)" -ForegroundColor Red }
    $finalResults=@($script:Results)
    $failed=@($finalResults | Where-Object { $_.passed -ne $true }).Count
    $passed=@($finalResults | Where-Object { $_.passed -eq $true }).Count
    $summaryColor=if($failed -eq 0){'Green'}else{'Yellow'}
    Write-Host "`nVerification finished: $passed passed / $failed failed." -ForegroundColor $summaryColor
    Write-Host "Detailed report: $MarkdownReport" -ForegroundColor Cyan
    Write-Host "Machine-readable report: $JsonReport" -ForegroundColor DarkGray
    Write-Host "MASTER DEBUG LOG (send this file): $MasterDebug" -ForegroundColor Yellow
    Write-MhwMasterDebug -Root $Root -Area 'VERIFY' -Message ("Verification finished: "+$passed+" passed / "+$failed+" failed. Markdown="+$MarkdownReport+"; JSON="+$JsonReport)
    if($TranscriptStarted){Stop-Transcript | Out-Null}
    Stop-MhwMasterDebugSession -Root $Root -Area 'VERIFY' -Summary ("$passed passed / $failed failed")
    if($failed -gt 0){exit 2}else{exit 0}
}
