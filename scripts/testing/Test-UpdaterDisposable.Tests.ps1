# Lightweight harness contract tests. Does not launch the manager or updater.
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$scriptPath=Join-Path $PSScriptRoot 'Test-UpdaterDisposable.ps1'
$tokens=$null
$errors=$null
$ast=[Management.Automation.Language.Parser]::ParseFile($scriptPath,[ref]$tokens,[ref]$errors)
if($errors.Count){throw 'Harness parse failed.'}
# Load function declarations only; never execute profile/package/process orchestration.
foreach($function in $ast.FindAll({param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst]},$false)){
    . ([scriptblock]::Create($function.Extent.Text))
}
$fixture=Join-Path (Split-Path -Parent $PSScriptRoot) ('artifacts/disposable-contract-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $fixture -Force | Out-Null
$passed=0
function Reject([scriptblock]$Action,[string]$Expected) {
    $caught=$false
    try { & $Action } catch {
        if($_.Exception.Message -notlike ('*'+$Expected+'*')){throw}
        $caught=$true
    }
    if(-not $caught){throw ('Negative fixture unexpectedly passed: '+$Expected)}
    $script:passed++
}
$data=Join-Path $fixture 'owned.bin'
[IO.File]::WriteAllText($data,'original')
$product=@{files=@(@{path='owned.bin';size=8;sha256=(Hash $data)})}
Assert-Owned $fixture $product
$passed++
[IO.File]::WriteAllText($data,'modified')
Reject { Assert-Owned $fixture $product } 'Owned bytes differ'
[IO.File]::WriteAllText($data,'original')
$product.files[0].size=9
Reject { Assert-Owned $fixture $product } 'Owned length differs'
$health=Join-Path $fixture 'health.json'
$record=@{token='token';attemptId='attempt';buildNumber=42;sourceSha=('a'*40);processId=$PID}
$manifest=@{buildNumber=42;sourceSha=('a'*40)}
$TimeoutSeconds=120
$script:runStarted=[DateTime]::UtcNow.AddDays(-1)
Write-Json $health $record
Reject { Wait-Health $health 'other-token' 'attempt' $manifest 'unused.exe' } 'token/attempt mismatch'
Reject { Wait-Health $health 'token' 'other-attempt' $manifest 'unused.exe' } 'token/attempt mismatch'
$manifest.buildNumber=43
Reject { Wait-Health $health 'token' 'attempt' $manifest 'unused.exe' } 'build identity mismatch'
$manifest.buildNumber=42
$manifest.sourceSha=('b'*40)
Reject { Wait-Health $health 'token' 'attempt' $manifest 'unused.exe' } 'build identity mismatch'
$manifest.sourceSha=('a'*40)
Reject { Wait-Health $health 'token' 'attempt' $manifest 'unused.exe' } 'executable path mismatch'
$current=Get-Process -Id $PID
$script:runStarted=[DateTime]::UtcNow.AddMinutes(1)
Reject { Wait-Health $health 'token' 'attempt' $manifest $current.Path } 'predates this run'
$script:runStarted=$current.StartTime.ToUniversalTime().AddSeconds(-1)
$confirmed=Wait-Health $health 'token' 'attempt' $manifest $current.Path
Assert-That ($confirmed.Id -eq $PID) 'Positive process identity fixture failed.'
$passed++
Assert-NoReparse $fixture
$passed++
Write-Host "PASS: $passed disposable harness contract checks (no updater/app process launched)."
# Retain tiny fixtures under ignored artifacts/; no recursive cleanup or profile writes.
