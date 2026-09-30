$ErrorActionPreference='Stop'
$Root=(Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
. (Join-Path $PSScriptRoot '..\diagnostics\Master-Debug.ps1')
Start-MhwMasterDebugSession -Root $Root -Area 'BENCHMARK' -Title 'Benchmark run'
Push-Location $Root
$ok=$false
try{
    dotnet run -c Release --project .\benchmarks\MhwModManager.Benchmarks\MhwModManager.Benchmarks.csproj -- --filter '*' 2>&1 | ForEach-Object{$line=$_.ToString();Write-Host $line;Write-MhwMasterDebug -Root $Root -Area 'DOTNET' -Message ('[Benchmark] '+$line)}
    if($LASTEXITCODE){throw "Benchmark run failed with exit code $LASTEXITCODE"}
    $ok=$true
}catch{
    Write-MhwMasterDebug -Root $Root -Area 'BENCHMARK-FATAL' -Message $_.Exception.ToString()
    throw
}finally{
    Pop-Location
    Stop-MhwMasterDebugSession -Root $Root -Area 'BENCHMARK' -Summary ($(if($ok){'PASS'}else{'FAIL'}))
}
