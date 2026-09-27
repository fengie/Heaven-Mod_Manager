$ErrorActionPreference='Stop'
# Exercise the actual fingerprint functions without running the release pipeline.
$tokens=$null
$errors=$null
$ast=[System.Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot 'Verify-Release.ps1'),[ref]$tokens,[ref]$errors)
if($errors.Count -gt 0){throw 'Verifier script must parse before testing its cache.'}
foreach($name in @('Add-ProjectInputs','Get-ProjectFingerprint')){
    $definition=$ast.Find({param($node) $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq $name},$true)
    if($null -eq $definition){throw "Missing fingerprint function: $name"}
    . ([scriptblock]::Create($definition.Extent.Text))
}
$Root=Join-Path ([IO.Path]::GetTempPath()) ('mhw-cache-test-'+[guid]::NewGuid().ToString('N'))
$sdk='fixture-toolchain'
try{
    foreach($folder in @('src/Core','src/App','tools/MhwModManager.FunctionVerifier','tests/MhwModManager.IntegrationTests','scripts','_AGENT_CONTEXT')){
        New-Item -ItemType Directory -Force -Path (Join-Path $Root $folder) | Out-Null
    }
    $core='src/Core/Core.csproj'
    $integration='tests/MhwModManager.IntegrationTests/MhwModManager.IntegrationTests.csproj'
    Set-Content -LiteralPath (Join-Path $Root $core) -Value '<Project />'
    Set-Content -LiteralPath (Join-Path $Root $integration) -Value '<Project><ItemGroup><ProjectReference Include="../../src/Core/Core.csproj" /></ItemGroup></Project>'
    foreach($path in @('NEXT-AGENT-START-HERE.md','_AGENT_CONTEXT/CONTINUITY_PROTOCOL.md','_AGENT_CONTEXT/handoff-manifest.json','src/App/MainWindow.xaml','scripts/Fixture.ps1')){
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
    Write-Host 'PASS: Verification cache regressions (XAML, scripts, generated files, unrelated Core, build targets).'
}
finally{
    if(Test-Path $Root){Remove-Item -LiteralPath $Root -Recurse -Force}
}
