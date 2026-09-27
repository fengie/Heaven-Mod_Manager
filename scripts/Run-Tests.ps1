$ErrorActionPreference='Stop'
$Root=(Resolve-Path (Join-Path $PSScriptRoot '..')).Path
. (Join-Path $PSScriptRoot 'Master-Debug.ps1')
Start-MhwMasterDebugSession -Root $Root -Area 'RUN-TESTS' -Title 'Standalone test run'
Push-Location $Root
$ok=$false
try{
    Write-MhwMasterDebug -Root $Root -Area 'RUN-TESTS' -Message 'Starting Core tests.'
    dotnet test .\tests\MhwModManager.Tests\MhwModManager.Tests.csproj -c Release 2>&1 | ForEach-Object{$line=$_.ToString();Write-Host $line;Write-MhwMasterDebug -Root $Root -Area 'DOTNET' -Message ('[Core tests] '+$line)}
    if($LASTEXITCODE){throw "Core tests failed with exit code $LASTEXITCODE"}
    if($env:OS -eq 'Windows_NT'){
        Write-MhwMasterDebug -Root $Root -Area 'RUN-TESTS' -Message 'Starting Integration tests.'
        dotnet test .\tests\MhwModManager.IntegrationTests\MhwModManager.IntegrationTests.csproj -c Release 2>&1 | ForEach-Object{$line=$_.ToString();Write-Host $line;Write-MhwMasterDebug -Root $Root -Area 'DOTNET' -Message ('[Integration tests] '+$line)}
        if($LASTEXITCODE){throw "Integration tests failed with exit code $LASTEXITCODE"}
    }
    $ok=$true
}catch{
    Write-MhwMasterDebug -Root $Root -Area 'RUN-TESTS-FATAL' -Message $_.Exception.ToString()
    throw
}finally{
    Pop-Location
    Stop-MhwMasterDebugSession -Root $Root -Area 'RUN-TESTS' -Summary ($(if($ok){'PASS'}else{'FAIL'}))
}
