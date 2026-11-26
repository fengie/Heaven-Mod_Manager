$ErrorActionPreference='Stop'
# Exercise the actual fingerprint functions without running the release pipeline.
$tokens=$null
$errors=$null
$ast=[System.Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot '..\release\Verify-Release.ps1'),[ref]$tokens,[ref]$errors)
if($errors.Count -gt 0){throw 'Verifier script must parse before testing its cache.'}
foreach($name in @('Add-ProjectInputs','Get-ProjectFingerprint','Get-StageFingerprint','Get-CachedStage','Initialize-StageCache','Set-CachedStage','Invoke-CachedDotnetStep','Invoke-BuildAndTestStages','Add-BlockedStage')){
    $definition=$ast.Find({param($node) $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq $name},$true)
    if($null -eq $definition){throw "Missing fingerprint function: $name"}
    . ([scriptblock]::Create($definition.Extent.Text))
}
$strictCompile=$ast.Find({
    param($node)
    $node -is [System.Management.Automation.Language.CommandAst] -and
        $node.GetCommandName() -eq 'Invoke-CachedDotnetStep' -and
        $node.Extent.Text -match 'Strict compile/analyzers'
},$true)
if($null -eq $strictCompile -or $strictCompile.Extent.Text -notmatch "'--no-incremental'"){
    throw 'Strict compile/analyzers must force recompilation after the relaxed build so incremental up-to-date checks cannot suppress diagnostics.'
}
$workflow=Get-Content -Raw -LiteralPath (Join-Path $PSScriptRoot '..\..\.github\workflows\workflow-feature-pr-gate.yml')
foreach($required in @('scripts/release/Verify-Release.ps1','scripts/testing/Test-VerificationCache.ps1','docs/verification/**')){
    if(!$workflow.Contains("'$required'")){throw "Feature gate does not cover verification input: $required"}
}
$Root=Join-Path ([IO.Path]::GetTempPath()) ('mhw-cache-test-'+[guid]::NewGuid().ToString('N'))
$sdk='fixture-toolchain'
$oldCi=$env:CI
$oldGithubActions=$env:GITHUB_ACTIONS
try{
    foreach($folder in @('src/Core','src/App','tests/MhwModManager.FunctionVerifier','tests/MhwModManager.IntegrationTests','scripts/release','_AGENT_CONTEXT','docs/verification','.github/workflows','data','tests/fixtures','.verification')){
        New-Item -ItemType Directory -Force -Path (Join-Path $Root $folder) | Out-Null
    }
    $core='src/Core/Core.csproj'
    $integration='tests/MhwModManager.IntegrationTests/MhwModManager.IntegrationTests.csproj'
    Set-Content -LiteralPath (Join-Path $Root $core) -Value '<Project />'
    Set-Content -LiteralPath (Join-Path $Root $integration) -Value '<Project><ItemGroup><ProjectReference Include="../../src/Core/Core.csproj" /></ItemGroup></Project>'
    foreach($path in @('NEXT-AGENT-START-HERE.md','_AGENT_CONTEXT/CONTINUITY_PROTOCOL.md','_AGENT_CONTEXT/handoff-manifest.json','src/App/MainWindow.xaml','scripts/Fixture.ps1','scripts/release/Verify-Release.ps1','docs/verification/wpf-control-inventory.txt','docs/verification/wpf-control-behavior.tsv','docs/verification/feature-test-matrix.json','.github/workflows/windows-release-gate.yml','data/fixture.json','tests/fixtures/game.txt','README.md','VERSION.txt','.verification/trusted-v8.7.0-files.json')){
        Set-Content -LiteralPath (Join-Path $Root $path) -Value 'initial'
    }
    $coreBefore=Get-ProjectFingerprint $core
    $before=Get-ProjectFingerprint $integration
    Set-Content -LiteralPath (Join-Path $Root 'src/App/MainWindow.xaml') -Value 'changed XAML'
    $after=Get-ProjectFingerprint $integration
    if($before -eq $after){throw 'Integration fingerprint ignored an App/XAML edit.'}
    if($coreBefore -ne (Get-ProjectFingerprint $core)){throw 'Unrelated App edits invalidated Core.'}
    $before=$after
    Set-Content -LiteralPath (Join-Path $Root 'scripts/Fixture.ps1') -Value 'changed script'
    $after=Get-ProjectFingerprint $integration
    if($before -eq $after){throw 'Integration fingerprint ignored a script edit.'}
    New-Item -ItemType Directory -Force -Path (Join-Path $Root 'src/App/obj') | Out-Null
    Set-Content -LiteralPath (Join-Path $Root 'src/App/obj/Generated.cs') -Value 'generated'
    if($after -ne (Get-ProjectFingerprint $integration)){throw 'Generated output invalidated the cache.'}
    Set-Content -LiteralPath (Join-Path $Root 'Directory.Build.targets') -Value '<Project />'
    if($coreBefore -eq (Get-ProjectFingerprint $core)){throw 'Common build targets did not invalidate Core.'}
    $coreBefore=Get-ProjectFingerprint $core
    foreach($relative in @('docs/verification/wpf-control-inventory.txt','docs/verification/wpf-control-behavior.tsv','docs/verification/feature-test-matrix.json','.github/workflows/windows-release-gate.yml','data/fixture.json','tests/fixtures/game.txt','README.md','VERSION.txt','.verification/trusted-v8.7.0-files.json')){
        $before=Get-ProjectFingerprint $integration
        Set-Content -LiteralPath (Join-Path $Root $relative) -Value 'changed external verification input'
        if($before -eq (Get-ProjectFingerprint $integration)){throw "Integration fingerprint ignored $relative"}
    }
    $before=Get-ProjectFingerprint $integration
    Remove-Item -LiteralPath (Join-Path $Root 'docs/verification/wpf-control-inventory.txt')
    if($before -eq (Get-ProjectFingerprint $integration)){throw 'Removing an inventory input did not invalidate integration evidence.'}
    if($coreBefore -ne (Get-ProjectFingerprint $core)){throw 'Unrelated integration runtime inputs invalidated Core.'}

    $buildArgs=@('build',$core,'-c','Release','--no-incremental','-warnaserror')
    $fingerprint=Get-StageFingerprint $core $buildArgs
    if($fingerprint -ne (Get-StageFingerprint $core $buildArgs)){throw 'Identical invocation fingerprint is unstable.'}
    if($fingerprint -eq (Get-StageFingerprint $core @('build',$core,'-c','Release','--no-incremental'))){throw 'Stage fingerprint ignored strict analyzer arguments.'}
    if($fingerprint -eq (Get-StageFingerprint $core @('test',$core,'-c','Release','--no-build'))){throw 'Stage fingerprint ignored the command.'}
    Set-Content -LiteralPath (Join-Path $Root 'scripts/release/Verify-Release.ps1') -Value 'changed policy'
    if($fingerprint -eq (Get-StageFingerprint $core $buildArgs)){throw 'Stage fingerprint ignored verifier policy.'}

    $env:CI=''
    $env:GITHUB_ACTIONS=''
    $script:StageCache=@{ fixture=[pscustomobject]@{id='fixture';fingerprint=$fingerprint;verified=$true} }
    if($null -eq (Get-CachedStage 'fixture' $fingerprint)){throw 'Identical local verified evidence was not reusable.'}
    if($null -ne (Get-CachedStage 'missing' $fingerprint)){throw 'Missing stage was credited.'}
    $script:StageCache['fixture'].verified='true'
    if($null -ne (Get-CachedStage 'fixture' $fingerprint)){throw 'Non-boolean verification was credited.'}
    $script:StageCache['fixture'].verified=$true
    foreach($ci in @('CI','GITHUB_ACTIONS')){
        [Environment]::SetEnvironmentVariable($ci,'true','Process')
        if($null -ne (Get-CachedStage 'fixture' $fingerprint)){throw "$ci reused historical evidence."}
        [Environment]::SetEnvironmentVariable($ci,'','Process')
    }

    $StageStatusPath=Join-Path $Root '.verification/stage-status.json'
    $entry=@{id='fixture';fingerprint=$fingerprint;verified=$true}
    @{formatVersion=1;stages=@($entry,$entry)} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $StageStatusPath
    $rejected=$false
    try { Initialize-StageCache } catch { $rejected=$true }
    if(!$rejected){throw 'Duplicate cache stage identity was accepted.'}

    # Execute the actual stage orchestration with controlled native outcomes.
    # No product builds, processes, or host files are needed for these fixtures.
    function Write-MhwMasterDebug { param($Root,$Area,$Message) }
    function Add-Result {
        param($Group,$Name,$Passed,$ExitCode,$Seconds,$Log,$Detail)
        $script:FixtureResults += [pscustomobject]@{group=$Group;name=$Name;passed=$Passed;detail=$Detail}
    }
    function Invoke-DotnetStep {
        param($Group,$Name,$Arguments,$LogName)
        $script:FixtureCalls += $Name
        $script:FixtureArguments=@($Arguments)
        return ($Name -ne $script:FixtureFailure)
    }
    function Save-StageCache { $script:FixtureCacheWrites++ }
    $script:FixtureCalls=@()
    $script:FixtureResults=@()
    $script:FixtureCacheWrites=0
    $script:FixtureFailure='cache fixture'
    $currentFingerprint=Get-StageFingerprint $core $buildArgs
    $script:StageCache=@{ fixture=[pscustomobject]@{id='fixture';fingerprint=$currentFingerprint;verified=$true} }
    $env:CI='true'
    if(Invoke-CachedDotnetStep 'fixture' $core 'Tests' 'cache fixture' $buildArgs 'fixture-log'){throw 'Failed fresh execution returned success.'}
    if($script:StageCache.ContainsKey('fixture') -or $script:FixtureCacheWrites -ne 1){throw 'Observed stage failure did not invalidate its positive cache entry.'}
    $script:FixtureFailure=''
    if(!(Invoke-CachedDotnetStep 'fixture' $core 'Tests' 'cache fixture' $buildArgs 'fixture-log' -Binlog 'owned.binlog')){throw 'Successful fresh execution was not credited.'}
    if($script:StageCache['fixture'].fingerprint -cne $currentFingerprint -or $script:FixtureCacheWrites -ne 2){throw 'Stage cache did not retain exact fresh invocation evidence.'}
    if($script:FixtureArguments[-1] -ne '-bl:owned.binlog'){throw 'Diagnostic binlog argument was lost.'}
    $env:CI=''
    $beforeCalls=$script:FixtureCalls.Count
    if(!(Invoke-CachedDotnetStep 'fixture' $core 'Tests' 'cache fixture' $buildArgs 'fixture-log' -Binlog 'another-owned.binlog')){throw 'Identical local stage was not reusable.'}
    if($script:FixtureCalls.Count -ne $beforeCalls){throw 'Generated binlog output path invalidated semantic evidence.'}

    # Substitute only the native command boundary for dependency tests.
    function Invoke-CachedDotnetStep {
        param($CacheId,$ProjectPath,$Group,$Name,$Arguments,$LogName,$Binlog)
        $script:FixtureCalls += $Name
        return ($Name -ne $script:FixtureFailure)
    }
    $BuildLogs=Join-Path $Root 'logs'
    $Stamp='fixture'
    $RelaxedBinlog=Join-Path $BuildLogs 'relaxed.binlog'
    $FinalBinlog=Join-Path $BuildLogs 'strict.binlog'
    $FunctionBaseline='function-baseline.json'
    $TrustedFunctionFiles='trusted-files.json'
    $TrustedFunctionSource='trusted-source.zip'
    $FunctionReport='function-report.json'
    $Projects=@(@{Name='FixtureCore';Path=$core},@{Name='FixtureApp';Path=$integration})
    $testNames=@('Core unit tests','Automation unit tests','Integration + fault injection','Full automation self-test')
    foreach($failure in @('Solution restore','Relaxed whole solution','FixtureCore','Strict whole solution','')){
        $script:FixtureCalls=@()
        $script:FixtureResults=@()
        $script:FixtureFailure=$failure
        $ready=Invoke-BuildAndTestStages -WindowsHost $true
        if($failure){
            if($ready){throw "Failed build prerequisite was credited: $failure"}
            foreach($name in $testNames){
                if($script:FixtureCalls -contains $name){throw "Test used unverified binaries after $failure : $name"}
                if(@($script:FixtureResults | Where-Object { $_.name -eq $name -and $_.passed -eq $false }).Count -ne 1){throw "Blocked test was not reported as failed: $name"}
            }
            if($failure -eq 'Solution restore' -and $script:FixtureCalls -contains 'Relaxed whole solution'){throw 'Restore failure ran dependent compile.'}
            if($failure -eq 'FixtureCore' -and $script:FixtureCalls -notcontains 'FixtureApp'){throw 'Independent strict diagnostics were lost.'}
        }else{
            if(!$ready){throw 'Successful build prerequisites failed to admit tests.'}
            foreach($name in $testNames){if($script:FixtureCalls -notcontains $name){throw "Required test was not executed: $name"}}
        }
    }
    Write-Host 'PASS: Verification cache regressions (complete runtime inputs, invocation/policy, fresh CI, duplicate identity, failed prerequisite handling, strict diagnostics).'

}
finally{
    $env:CI=$oldCi
    $env:GITHUB_ACTIONS=$oldGithubActions
    if(Test-Path $Root){Remove-Item -LiteralPath $Root -Recurse -Force}
}
