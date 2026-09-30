# Run only in a fresh disposable Windows account/VM. No release publication or downloads.
[CmdletBinding()]
param(
    [string]$OldArtifactPath,
    [string]$OldManifestPath,
    [string]$NewArtifactPath,
    [string]$NewManifestPath,
    [switch]$DisposableProfile,
    [ValidateRange(120,600)][int]$TimeoutSeconds=180
)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest

function Assert-That($Condition,[string]$Message) { if(-not $Condition){throw $Message} }
function Read-Json([string]$Path) { Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json }
function Write-Json([string]$Path,$Value) {
    [IO.File]::WriteAllText($Path,($Value | ConvertTo-Json -Depth 30),[Text.UTF8Encoding]::new($false))
}
function Hash([string]$Path) { (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash }
function Assert-NoReparse([string]$Path) {
    $cursor=[IO.Path]::GetFullPath($Path)
    while($cursor){
        if(Test-Path -LiteralPath $cursor){
            Assert-That (((Get-Item -LiteralPath $cursor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) -eq 0) 'Reparse ancestor refused.'
        }
        $cursor=Split-Path -Parent $cursor
    }
}
function Start-Fixture([string]$Executable,[string[]]$Arguments,[string]$Home,[string]$Game) {
    Assert-NoReparse $Executable
    $start=[Diagnostics.ProcessStartInfo]::new($Executable)
    $start.UseShellExecute=$false
    $start.CreateNoWindow=$true
    $start.WindowStyle=[Diagnostics.ProcessWindowStyle]::Hidden
    $start.WorkingDirectory=$Home
    # Only fixed flags, GUIDs and generated paths are accepted here; quote paths for Windows PS 5.1.
    foreach($arg in $Arguments){ Assert-That (-not $arg.Contains('"')) 'Quote in process argument refused.' }
    $start.Arguments=($Arguments | ForEach-Object { '"'+$_+'"' }) -join ' '
    $start.EnvironmentVariables['MOD_MANAGER_HOME']=$Home
    $start.EnvironmentVariables['MHW_MANAGER_HOME']=$Home
    $start.EnvironmentVariables['MOD_MANAGER_GAME_ROOT']=$Game
    $start.EnvironmentVariables['MHW_GAME_ROOT']=$Game
    [void]$start.EnvironmentVariables.Remove('MHW_MOD_MANAGER_GITHUB_TOKEN')
    [Diagnostics.Process]::Start($start)
}
function Health-Arguments([string]$File,[string]$Token,[string]$Attempt) {
    @('--mhw-update-health-file',$File,'--mhw-update-health-token',$Token,'--mhw-update-health-attempt',$Attempt)
}
function Wait-Health([string]$File,[string]$Token,[string]$Attempt,$Manifest,[string]$Executable) {
    $deadline=[DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    while([DateTime]::UtcNow -lt $deadline){
        if(Test-Path -LiteralPath $File){
            $record=Read-Json $File
            Assert-That ($record.token -ceq $Token -and $record.attemptId -ceq $Attempt) 'Health token/attempt mismatch.'
            Assert-That ($record.buildNumber -eq $Manifest.buildNumber -and $record.sourceSha -eq $Manifest.sourceSha) 'Restarted build identity mismatch.'
            $process=Get-Process -Id $record.processId -ErrorAction Stop
            Assert-That ($process.Path -ieq $Executable) 'Health PID executable path mismatch.'
            Assert-That ($process.StartTime.ToUniversalTime() -ge $script:runStarted) 'Health PID predates this run.'
            return $process
        }
        Start-Sleep -Milliseconds 200
    }
    throw 'Timed out awaiting actual app startup acknowledgement.'
}
function Stop-Fixture($Process,[string]$Executable) {
    $Process.Refresh()
    if(-not $Process.HasExited){
        Assert-That ($Process.Path -ieq $Executable) 'Refusing to stop unexpected executable.'
        $Process.Kill()
        Assert-That ($Process.WaitForExit(15000)) 'Fixture process did not exit.'
    }
}
function Assert-Owned([string]$Root,$Product) {
    foreach($entry in $Product.files){
        $path=Join-Path $Root $entry.path
        Assert-That ((Hash $path) -eq $entry.sha256) ('Owned bytes differ: '+$entry.path)
        Assert-That ((Get-Item -LiteralPath $path).Length -eq $entry.size) ('Owned length differs: '+$entry.path)
    }
}

Assert-That $DisposableProfile.IsPresent 'Requires -DisposableProfile in a fresh disposable Windows account/VM.'
Assert-That ([Environment]::OSVersion.Platform -eq [PlatformID]::Win32NT) 'Windows required.'
foreach($inputPath in @($OldArtifactPath,$OldManifestPath,$NewArtifactPath,$NewManifestPath)){
    Assert-That (-not [string]::IsNullOrWhiteSpace($inputPath)) 'Four explicit package/manifest paths are required.'
    Assert-That (Test-Path -LiteralPath $inputPath -PathType Leaf) 'Required package input is missing.'
}
$profileRoot=Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'MhwModManager'
Assert-NoReparse $profileRoot
Assert-That (-not (Test-Path -LiteralPath $profileRoot)) 'Existing manager profile refused; use a fresh disposable account.'
# Verify exact packages before any app launch. Existing verifier owns its temporary extraction.
& "$PSScriptRoot/Test-UpdaterPackage.ps1" -ArtifactPath $OldArtifactPath -ManifestPath $OldManifestPath
& "$PSScriptRoot/Test-UpdaterPackage.ps1" -ArtifactPath $NewArtifactPath -ManifestPath $NewManifestPath
$old=Read-Json $OldManifestPath
$new=Read-Json $NewManifestPath
Assert-That ($new.buildNumber -gt $old.buildNumber) 'Target build must be strictly newer.'
Assert-That ($new.sourceSha -ne $old.sourceSha) 'Old/new source identities must differ.'
$script:runStarted=[DateTime]::UtcNow
$run=Join-Path $profileRoot ('Updater/disposable-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $run | Out-Null
$results=@()
Add-Type -AssemblyName System.IO.Compression.FileSystem
try {
    foreach($scenario in @('upgrade','startup-failure-rollback')){
        $case=Join-Path $run $scenario
        $install=Join-Path $case 'install'
        $stage=Join-Path $case 'payload'
        $helperRoot=Join-Path $case 'helper'
        $game=Join-Path $case 'game'
        New-Item -ItemType Directory -Path $case,$game | Out-Null
        [IO.Compression.ZipFile]::ExtractToDirectory((Resolve-Path -LiteralPath $OldArtifactPath).Path,$install)
        [IO.Compression.ZipFile]::ExtractToDirectory((Resolve-Path -LiteralPath $NewArtifactPath).Path,$stage)
        Copy-Item -LiteralPath (Join-Path $install 'UpdaterHelper') -Destination $helperRoot -Recurse
        # Only a presence fixture for game discovery; never launched or deployed to.
        [IO.File]::WriteAllText((Join-Path $game 'MonsterHunterWorld.exe'),'disposable game-presence fixture')
        $seeds=@{}
        foreach($relative in @('Mods/disposable-seed/bytes.bin','State/disposable-seed.bin','unknown-user-file.bin')){
            $path=Join-Path $install $relative
            New-Item -ItemType Directory -Path (Split-Path -Parent $path) -Force | Out-Null
            [IO.File]::WriteAllText($path,('seed-'+[Guid]::NewGuid().ToString('N')))
            $seeds[$relative]=Hash $path
        }
        $oldProduct=Read-Json (Join-Path $install 'product-files.json')
        $oldMetadata=@{}
        foreach($name in @('product-files.json','release-install.json')){ $oldMetadata[$name]=Hash (Join-Path $install $name) }
        $oldExe=Join-Path $install $old.executableRelativePath
        $initialHealth=Join-Path $case 'initial-health.json'
        $initialToken=[Guid]::NewGuid().ToString('N')
        $initial=Start-Fixture $oldExe (Health-Arguments $initialHealth $initialToken 'initial') $install $game
        $healthy=Wait-Health $initialHealth $initialToken 'initial' $old $oldExe
        Assert-That ($healthy.Id -eq $initial.Id) 'Initial old app PID mismatch.'
        Stop-Fixture $initial $oldExe

        $target=Read-Json $NewManifestPath
        $restart=@()
        $rollbackHealth=Join-Path $case 'rollback-health.json'
        $rollbackToken=[Guid]::NewGuid().ToString('N')
        $faultHash=$null
        if($scenario -eq 'startup-failure-rollback'){
            # Controlled fixture: fail the actual apphost before managed startup. Never alter input ZIPs.
            $configs=@(Get-ChildItem -LiteralPath $stage -File -Filter '*.runtimeconfig.json')
            Assert-That ($configs.Count -eq 1) 'Expected exactly one root app runtimeconfig.'
            [IO.File]::WriteAllText($configs[0].FullName,'{ invalid disposable startup fixture')
            $faultHash=Hash $configs[0].FullName
            $product=Read-Json (Join-Path $stage 'product-files.json')
            $entry=@($product.files | Where-Object { $_.path -eq $configs[0].Name })
            Assert-That ($entry.Count -eq 1) 'Runtimeconfig must be product owned.'
            $entry[0].size=(Get-Item -LiteralPath $configs[0].FullName).Length
            $entry[0].sha256=$faultHash
            Write-Json (Join-Path $stage 'product-files.json') $product
            $target.productManifestSha256=Hash (Join-Path $stage 'product-files.json')
            $marker=Read-Json (Join-Path $stage 'release-install.json')
            $marker.productManifestSha256=$target.productManifestSha256
            Write-Json (Join-Path $stage 'release-install.json') $marker
            $restart=Health-Arguments $rollbackHealth $rollbackToken 'restored-old'
        }
        $health=Join-Path $case 'target-health.json'
        $token=[Guid]::NewGuid().ToString('N')
        $journal=Join-Path $case 'journal.json'
        $request=@{manifest=$target;installRoot=$install;stagingRoot=$stage;backupRoot=(Join-Path $case 'backup');journalPath=$journal;pendingPath=(Join-Path $case 'pending.json');healthFile=$health;healthToken=$token;currentProcessId=0;restartArguments=@($restart)}
        $requestPath=Join-Path $case 'request.json'
        Write-Json $requestPath $request
        $helperExe=Join-Path $helperRoot 'MHW Mod Manager Updater.exe'
        $helper=Start-Fixture $helperExe @('--request',$requestPath) $install $game
        Assert-That ($helper.WaitForExit($TimeoutSeconds*1000)) 'Helper timed out; preserve VM and recovery evidence.'
        $state=Read-Json $journal
        if($scenario -eq 'upgrade'){
            Assert-That ($helper.ExitCode -eq 0 -and $state.phase -eq 7) 'Expected helper exit 0 and Confirmed journal.'
            $ack=Read-Json $health
            $restarted=Wait-Health $health $token $ack.attemptId $new (Join-Path $install $new.executableRelativePath)
            Assert-Owned $install (Read-Json (Join-Path $stage 'product-files.json'))
            foreach($name in @('product-files.json','release-install.json')){
                Assert-That ((Hash (Join-Path $install $name)) -eq (Hash (Join-Path $stage $name))) 'Updated metadata bytes differ.'
            }
        } else {
            Assert-That ($helper.ExitCode -eq 5 -and $state.phase -eq 9) 'Expected helper exit 5 and RolledBack journal.'
            Assert-That (-not (Test-Path -LiteralPath $health)) 'Failed startup unexpectedly acknowledged target health.'
            $restarted=Wait-Health $rollbackHealth $rollbackToken 'restored-old' $old $oldExe
            Assert-Owned $install $oldProduct
            foreach($name in $oldMetadata.Keys){ Assert-That ((Hash (Join-Path $install $name)) -eq $oldMetadata[$name]) 'Rollback metadata bytes differ.' }
            foreach($entry in (Read-Json (Join-Path $stage 'product-files.json')).files){
                if($entry.path -notin @($oldProduct.files.path)){
                    Assert-That (-not (Test-Path -LiteralPath (Join-Path $install $entry.path))) 'New-only owned path survived rollback.'
                }
            }
        }
        Stop-Fixture $restarted $restarted.Path
        foreach($relative in $seeds.Keys){ Assert-That ((Hash (Join-Path $install $relative)) -eq $seeds[$relative]) ('Seed changed: '+$relative) }
        $results+=@{scenario=$scenario;result='PASS';helperExitCode=$helper.ExitCode;journalPhase=$state.phase;restartedBuild=(Read-Json (Join-Path $install 'build-identity.json'));seedHashes=$seeds;injectedRuntimeConfigSha256=$faultHash}
        Write-Json (Join-Path $run 'summary.json') @{oldArtifactSha256=(Hash $OldArtifactPath);newArtifactSha256=(Hash $NewArtifactPath);cases=$results;status='in-progress'}
    }
    Write-Json (Join-Path $run 'summary.json') @{oldArtifactSha256=(Hash $OldArtifactPath);newArtifactSha256=(Hash $NewArtifactPath);cases=$results;status='PASS'}
    Write-Host 'PASS: disposable helper upgrade and injected startup-failure rollback; summary.json retained in disposable profile.'
} catch {
    Write-Json (Join-Path $run 'summary.json') @{status='FAIL';completedCases=$results;errorType=$_.Exception.GetType().Name;note='Raw logs and processes retained in disposable VM for diagnosis; do not export unsanitized logs.'}
    throw
}
# Deliberately no recursive cleanup: retain evidence; discard the disposable VM/account externally.
