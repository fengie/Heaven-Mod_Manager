param([switch]$KeepBuildOutputs)

$ErrorActionPreference='Stop'
$Root=(Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
& (Join-Path $PSScriptRoot 'Clear-StaleTestScratch.ps1') -MinimumAgeHours 24
. (Join-Path $PSScriptRoot 'AcceptanceScratch.ps1')
. (Join-Path $PSScriptRoot '..\diagnostics\Master-Debug.ps1')
Start-MhwMasterDebugSession -Root $Root -Area 'RUN-TESTS' -Title 'Standalone test run'
Push-Location $Root
$ok=$false
$scratch=$null
$buildArguments=@()
try{
    if (!$KeepBuildOutputs) {
        # Some tests locate fixture sources by walking up from their assembly.
        $scratchRoot = Join-Path $Root 'artifacts'
        if (!(Test-Path -LiteralPath $scratchRoot)) {
            New-Item -ItemType Directory -Path $scratchRoot -Force | Out-Null
        }
        $scratch = New-MhwAcceptanceScratch -ScratchRoot $scratchRoot
        $buildArguments = @('--artifacts-path', $scratch.Path)
    }
    Write-MhwMasterDebug -Root $Root -Area 'RUN-TESTS' -Message 'Starting Core tests.'
    dotnet test --project .\tests\MhwModManager.Tests\MhwModManager.Tests.csproj -c Release @buildArguments 2>&1 | ForEach-Object{$line=$_.ToString();Write-Host $line;Write-MhwMasterDebug -Root $Root -Area 'DOTNET' -Message ('[Core tests] '+$line)}
    if($LASTEXITCODE){throw "Core tests failed with exit code $LASTEXITCODE"}
    if([Environment]::OSVersion.Platform -eq [PlatformID]::Win32NT){
        Write-MhwMasterDebug -Root $Root -Area 'RUN-TESTS' -Message 'Starting Integration tests.'
        dotnet test --project .\tests\MhwModManager.IntegrationTests\MhwModManager.IntegrationTests.csproj -c Release @buildArguments 2>&1 | ForEach-Object{$line=$_.ToString();Write-Host $line;Write-MhwMasterDebug -Root $Root -Area 'DOTNET' -Message ('[Integration tests] '+$line)}
        if($LASTEXITCODE){throw "Integration tests failed with exit code $LASTEXITCODE"}
    }
    $ok=$true
}catch{
    Write-MhwMasterDebug -Root $Root -Area 'RUN-TESTS-FATAL' -Message $_.Exception.ToString()
    throw
}finally{
    try {
        if ($null -ne $scratch) { Remove-MhwAcceptanceScratch $scratch -ScratchRoot $scratchRoot }
    } catch {
        if ($ok) {
            $ok = $false
            throw
        }
        Write-Warning ('Standalone test build cleanup failed: ' + $_.Exception.Message)
    } finally {
        Pop-Location
        Stop-MhwMasterDebugSession -Root $Root -Area 'RUN-TESTS' -Summary ($(if($ok){'PASS'}else{'FAIL'}))
    }
}
