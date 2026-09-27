# Run on Windows: powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Test-Manager.ps1
$ErrorActionPreference='Stop'
$fixture=Join-Path $env:TEMP ('MHWManagerV7Test-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $fixture | Out-Null
try{
  Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Engine.ps1') -Destination $fixture
  Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Modules') -Destination $fixture -Recurse
  Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Armor Database.csv') -Destination $fixture
  $env:MHW_MANAGER_TEST_GAME_ROOT=Join-Path $fixture 'Game'
  . (Join-Path $fixture 'Engine.ps1')
  New-Item -ItemType Directory -Force -Path $NativeRoot,$ModsRoot | Out-Null

  # Parse every PowerShell source file using the real Windows PowerShell parser before behavioral tests.
  $parseFiles=@(Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.ps1' -File) + @(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'Modules') -Filter '*.ps1' -File)
  foreach($parseFile in $parseFiles){
    $tokens=$null;$errors=$null
    [void][System.Management.Automation.Language.Parser]::ParseFile($parseFile.FullName,[ref]$tokens,[ref]$errors)
    Assert (@($errors).Count -eq 0) ("PowerShell parser error in {0}: {1}" -f $parseFile.Name,((@($errors)|ForEach-Object{$_.Message}) -join '; '))
  }

  $key='nativePC\pl\f_equip\pl020_0000\body\mod\body.mod3'
  $key2='nativePC\pl\f_equip\pl020_0000\arm\mod\arm.mod3'
  $dest=Destination $key;$dest2=Destination $key2
  New-Item -ItemType Directory -Force -Path (Split-Path -Parent $dest),(Split-Path -Parent $dest2) | Out-Null
  [IO.File]::WriteAllText($dest,'UNMANAGED ORIGINAL')
  [IO.File]::WriteAllText($dest2,'UNMANAGED ARM')
  foreach($name in @('Armor A','Armor B')){
    foreach($pair in @(@('body','body.mod3'),@('arm','arm.mod3'))){
      $path=Join-Path (Join-Path $ModsRoot $name) ("nativePC\pl\f_equip\pl020_0000\$($pair[0])\mod")
      New-Item -ItemType Directory -Force -Path $path | Out-Null
      [IO.File]::WriteAllText((Join-Path $path $pair[1]),$name)
    }
  }

  $s=Initialize-State

  # Smart conflict classification: soft textures, inferred modular overlays, hard structures, saved rules, providers.
  $soft=Analyze-Conflict $s 'nativePC\pl\f_equip\mod_hepsy\f_skin_NM.tex' @([pscustomobject]@{name='Skin A';hash='aaa'},[pscustomobject]@{name='Skin B';hash='bbb'})
  Assert (!$soft.blocking -and $soft.kind -eq 'SHARED-TEXTURE' -and $soft.winner -eq 'Skin B') 'Shared texture should be a non-blocking priority overlap.'
  $patch=Analyze-Conflict $s $key @([pscustomobject]@{name='HPN Sexy Kulu-Ya-Ku';hash='aaa'},[pscustomobject]@{name='HPN Sexy Kulu-Ya-Ku - Skimpy Waist';hash='bbb'})
  Assert (!$patch.blocking -and $patch.kind -eq 'PATCH-OVERLAY' -and $patch.winner -eq 'HPN Sexy Kulu-Ya-Ku - Skimpy Waist') 'Base + child option inference failed.'
  $hard=Analyze-Conflict $s $key @([pscustomobject]@{name='Armor A';hash='aaa'},[pscustomobject]@{name='Armor B';hash='bbb'})
  Assert ($hard.blocking -and $hard.kind -eq 'HARD-STRUCTURAL') 'Different structural replacements must remain blocking.'
  $ruleState=Clone-State $s;Set-ModRelationMemory $ruleState 'Armor A' 'Armor B' 'overlay' 'Armor B'
  $rule=Analyze-Conflict $ruleState $key @([pscustomobject]@{name='Armor A';hash='aaa'},[pscustomobject]@{name='Armor B';hash='bbb'})
  Assert (!$rule.blocking -and $rule.kind -eq 'OVERLAY-RULE' -and $rule.winner -eq 'Armor B') 'Saved overlay rule failed.'
  $badState=Clone-State $s;Set-ModRelationMemory $badState 'Armor A' 'Armor B' 'incompatible' $null
  $bad=Analyze-Conflict $badState $key @([pscustomobject]@{name='Armor A';hash='aaa'},[pscustomobject]@{name='Armor B';hash='bbb'})
  Assert ($bad.blocking -and $bad.kind -eq 'INCOMPATIBLE') 'Incompatible relationship failed.'
  $providerState=Clone-State $s;Map-Set $providerState.resourceProviders 'nativepc\pl\f_equip\mod_hepsy' 'Skin A'
  $provider=Analyze-Conflict $providerState 'nativePC\pl\f_equip\mod_hepsy\f_skin_NM.tex' @([pscustomobject]@{name='Skin A';hash='aaa'},[pscustomobject]@{name='Skin B';hash='bbb'})
  Assert (!$provider.blocking -and $provider.kind -eq 'SHARED-PROVIDER' -and $provider.winner -eq 'Skin A') 'Pinned shared-resource provider failed.'

  # Optimized clone must isolate mutable top-level maps without serializing every captured mod snapshot.
  $cloneProbe=Clone-State $providerState
  Map-Set $cloneProbe.winners '__clone_probe__' 'Skin A'
  Assert (!(Map-Has $providerState.winners '__clone_probe__')) 'Optimized state clone leaked winner-map mutations into the source state.'

  # Multi-provider stress smoke: many outfit mods may share the same body textures. This verifies the
  # indexed conflict path and sparse relation lookup used by the GUI under heavy load.
  $perf=Clone-State $s
  for($i=0;$i -lt 120;$i++){
    $files=New-Map
    for($j=0;$j -lt 18;$j++){
      $sharedKey=('nativePC\pl\f_equip\mod_hepsy\stress_{0:D2}.tex' -f $j)
      Map-Set $files $sharedKey ('hash-'+($i%4)+'-'+$j)
    }
    Map-Set $files ('nativePC\stress\mod_{0:D3}.txt' -f $i) ('unique-'+$i)
    $name=('Stress Mod {0:D3}' -f $i)
    Map-Set $perf.mods $name ([pscustomobject]@{name=$name;capturedAt=(Get-Date).ToString('o');files=$files})
    $perf.order+=@($name)
  }
  $sw=[Diagnostics.Stopwatch]::StartNew()
  $perfIndex=Get-CandidateIndex $perf
  $perfGroups=@(Get-ConflictGroupsForUi $perf $perfIndex)
  $sw.Stop()
  Assert ($perfIndex.ContainsKey('nativePC\pl\f_equip\mod_hepsy\stress_00.tex')) 'Candidate index missed a shared stress path.'
  Assert (@($perfIndex['nativePC\pl\f_equip\mod_hepsy\stress_00.tex']).Count -eq 120) 'Candidate index lost providers under load.'
  Assert ($perfGroups.Count -ge 1) 'Conflict UI analysis returned no shared-resource group under load.'
  Write-Host ("PERF: 120 mods / 2,280 indexed file entries analyzed in {0} ms" -f $sw.ElapsedMilliseconds)

  # An incompatibility must still beat an old explicit winner, including on a many-provider path.
  Set-ModRelationMemory $perf 'Stress Mod 000' 'Stress Mod 119' 'incompatible' $null
  Map-Set $perf.winners 'nativePC\pl\f_equip\mod_hepsy\stress_00.tex' 'Stress Mod 000'
  $multiBad=Analyze-Conflict $perf 'nativePC\pl\f_equip\mod_hepsy\stress_00.tex' @($perfIndex['nativePC\pl\f_equip\mod_hepsy\stress_00.tex'])
  Assert ($multiBad.blocking -and $multiBad.kind -eq 'INCOMPATIBLE') 'Saved incompatibility was hidden by an explicit winner on a multi-provider path.'

  $a=Clone-State $s;Map-Set $a.mods 'Armor A' (Snapshot-Mod 'Armor A');$a.order+=@('Armor A')
  Apply-State $s $a -Description 'test enable A' | Out-Null
  Assert (([IO.File]::ReadAllText($dest)) -eq 'Armor A') 'Enable failed.'
  Assert (([IO.File]::ReadAllText($dest2)) -eq 'Armor A') 'Second-file enable failed.'

  $s=Load-State;$b=Clone-State $s;Map-Set $b.mods 'Armor B' (Snapshot-Mod 'Armor B');$b.order+=@('Armor B')
  $groups=@(Get-UnresolvedConflictGroups $b)
  Assert ($groups.Count -eq 1 -and $groups[0].items.Count -eq 2) 'Grouped conflict detection failed.'
  $autoBlocked=Commit-PlanAutomatic $s (Clone-State $b) 'automatic blocked fixture'
  Assert ($autoBlocked.blocked -and !$autoBlocked.success) 'GUI automatic deployment must stop cleanly on unresolved structural conflicts.'
  Assert (([IO.File]::ReadAllText($dest)) -eq 'Armor A') 'Blocked automatic deployment changed live files.'
  Map-Set $b.winners $key 'Armor B';Map-Set $b.winners $key2 'Armor B'
  Apply-State $s $b -Description 'test override B' | Out-Null
  Assert (([IO.File]::ReadAllText($dest)) -eq 'Armor B') 'Override failed.'
  Assert (([IO.File]::ReadAllText($dest2)) -eq 'Armor B') 'Second-file override failed.'

  $s=Load-State;$back=Clone-State $s;Map-Remove $back.mods 'Armor B';$back.order=@('Armor A');Map-Remove $back.winners $key;Map-Remove $back.winners $key2
  Apply-State $s $back -Description 'test disable B' | Out-Null
  Assert (([IO.File]::ReadAllText($dest)) -eq 'Armor A') 'Override restoration failed.'

  $s=Load-State;$off=Clone-State $s;Map-Remove $off.mods 'Armor A';$off.order=@()
  Apply-State $s $off -Description 'test disable A' | Out-Null
  Assert (([IO.File]::ReadAllText($dest)) -eq 'UNMANAGED ORIGINAL') 'Unmanaged restoration failed.'
  Assert (([IO.File]::ReadAllText($dest2)) -eq 'UNMANAGED ARM') 'Second unmanaged restoration failed.'
  $s=Load-State
  Assert (!(Map-Has $s.expected $key) -and !(Map-Has $s.bases $key)) 'Restored unmanaged path stayed owned by manager.'

  # A later enable must capture the CURRENT unmanaged bytes, not an old base from a prior enable.
  [IO.File]::WriteAllText($dest,'MANUAL NEW BASE')
  $again=Clone-State $s;Map-Set $again.mods 'Armor A' (Snapshot-Mod 'Armor A');$again.order+=@('Armor A')
  Apply-State $s $again -Description 'test recapture base' | Out-Null
  $s=Load-State;$off2=Clone-State $s;Map-Remove $off2.mods 'Armor A';$off2.order=@()
  Apply-State $s $off2 -Description 'test restore recaptured base' | Out-Null
  Assert (([IO.File]::ReadAllText($dest)) -eq 'MANUAL NEW BASE') 'Latest unmanaged base was not recaptured.'

  # Profiles, portable export/import, and coverage output.
  $s=Load-State;Save-Profile $s 'minimal-test';$s=Load-State
  Rename-Profile $s 'minimal-test' 'minimal-renamed';$s=Load-State
  $export=Export-Profile $s 'minimal-renamed'
  Assert (Test-Path -LiteralPath $export -PathType Leaf) 'Profile export failed.'
  Import-Profile $s $export 'minimal-imported';$s=Load-State
  Assert (Map-Has $s.profiles 'minimal-imported') 'Profile import failed.'
  $coverage=Write-CoveragePage $s $false
  Assert (Test-Path -LiteralPath $coverage -PathType Leaf) 'Coverage generation failed.'
  $summary=Get-ManagerSummary $s
  Assert ($summary.Installed -ge 2) 'GUI manager summary failed.'
  $support=Export-SupportBundle $s
  Assert (Test-Path -LiteralPath $support -PathType Leaf) 'Support bundle export failed.'

  # Missing snapshot must fail and roll back cleanly.
  $candidate=Clone-State $s;Map-Set $candidate.mods 'Armor A' (Snapshot-Mod 'Armor A');$candidate.order+=@('Armor A')
  $badHash=Map-Get (Map-Get $candidate.mods 'Armor A').files $key
  Remove-Item -LiteralPath (Blob-Path $badHash)
  $beforeText=[IO.File]::ReadAllText($dest)
  $failed=$false
  try{Apply-State $s $candidate -Description 'intentional missing blob'}catch{$failed=$true}
  Assert $failed 'Missing snapshot should stop deployment.'
  Assert (([IO.File]::ReadAllText($dest)) -eq $beforeText) 'Rollback did not restore original bytes.'
  Assert (!(Test-Path -LiteralPath (Join-Path $V2Root 'Pending'))) 'Rollback journal was not cleared.'

  # File/directory collision across two mods must be caught before deployment.
  $shapeA=Join-Path $ModsRoot 'Shape A';$shapeB=Join-Path $ModsRoot 'Shape B'
  New-Item -ItemType Directory -Force -Path (Join-Path $shapeA 'nativePC\collision'),(Join-Path $shapeB 'nativePC\collision\foo') | Out-Null
  [IO.File]::WriteAllText((Join-Path $shapeA 'nativePC\collision\foo'),'file')
  [IO.File]::WriteAllText((Join-Path $shapeB 'nativePC\collision\foo\bar.txt'),'child')
  $s=Load-State;$shape=Clone-State $s;Map-Set $shape.mods 'Shape A' (Snapshot-Mod 'Shape A');Map-Set $shape.mods 'Shape B' (Snapshot-Mod 'Shape B');$shape.order+=@('Shape A','Shape B')
  $shapeFailed=$false
  try{Build-DeploymentPlan $s $shape}catch{$shapeFailed=$true}
  Assert $shapeFailed 'File/directory collision was not detected.'

  # A COMMITTED pending transaction must be finalized, never rolled back.
  $pending=Join-Path $V2Root 'Pending';New-Item -ItemType Directory -Path $pending | Out-Null
  $current=Load-State;Atomic-Json (Join-Path $pending 'before-state.json') $current;Atomic-Json (Join-Path $pending 'after-state.json') $current
  $op=New-OperationId
  Atomic-Json (Join-Path $pending 'journal.json') ([ordered]@{operationId=$op;description='committed recovery fixture';startedAt=(Get-Date).ToString('o');stateExisted=$true;files=@(@{key=$key;existed=$true;index='file000000'})})
  [IO.File]::WriteAllText((Join-Path $pending 'file000000'),'ROLLBACK BYTE THAT MUST NOT WIN')
  [IO.File]::WriteAllText($dest,'COMMITTED LIVE BYTE')
  Set-Content -LiteralPath (Join-Path $pending 'COMMITTED') -Value $op -Encoding ASCII
  Recover-Transaction
  Assert (([IO.File]::ReadAllText($dest)) -eq 'COMMITTED LIVE BYTE') 'Committed recovery incorrectly rolled back live data.'
  Assert (!(Test-Path -LiteralPath $pending)) 'Committed recovery did not clear Pending.'
  Assert (Test-Path -LiteralPath (Join-Path $HistoryRoot $op) -PathType Container) 'Committed recovery did not finalize history.'

  # Legacy migration: A installs over unmanaged original, B overrides A.
  $migration=Join-Path $fixture 'Migration'
  $ToolRoot=$migration;$ModsRoot=Join-Path $migration 'Mods';$StateRoot=Join-Path $migration 'State';$V2Root=Join-Path $StateRoot 'V2';$BlobRoot=Join-Path $V2Root 'Blobs';$HistoryRoot=Join-Path $V2Root 'History';$StateFile=Join-Path $V2Root 'state.json'
  $GameRoot=Join-Path $migration 'Game';$NativeRoot=Join-Path $GameRoot 'nativePC'
  $dest=Destination $key
  New-Item -ItemType Directory -Force -Path (Split-Path -Parent $dest),$StateRoot,$ModsRoot | Out-Null
  [IO.File]::WriteAllText($dest,'Armor B')
  $rel=$key.Substring(9)
  foreach($name in @('Armor A','Armor B')){
    $source=Join-Path (Join-Path $ModsRoot $name) $key
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $source) | Out-Null
    [IO.File]::WriteAllText($source,$name)
  }
  $backupA=Join-Path $StateRoot 'Backups\backupA';$backupB=Join-Path $StateRoot 'Backups\backupB'
  foreach($backup in @($backupA,$backupB)){New-Item -ItemType Directory -Force -Path (Split-Path -Parent (Join-Path $backup $rel)) | Out-Null}
  [IO.File]::WriteAllText((Join-Path $backupA $rel),'UNMANAGED ORIGINAL')
  [IO.File]::WriteAllText((Join-Path $backupB $rel),'Armor A')
  $hA=File-Hash (Join-Path $backupB $rel);$hB=File-Hash $dest
  @{name='Armor A';backupRoot='backupA';files=@(@{relative=$rel;hash=$hA;owned=$true;backedUp=$true;previousOwner=$null})} | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $StateRoot 'Armor A.json')
  @{name='Armor B';backupRoot='backupB';files=@(@{relative=$rel;hash=$hB;owned=$true;backedUp=$true;previousOwner='Armor A'})} | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $StateRoot 'Armor B.json')
  $m=Initialize-State
  Assert ((Map-Get $m.expected $key) -eq $hB) 'Migration winner incorrect.'
  Assert (([IO.File]::ReadAllText((Blob-Path (Map-Get $m.bases $key)))) -eq 'UNMANAGED ORIGINAL') 'Migration original backup incorrect.'
  Assert (([IO.File]::ReadAllText($dest)) -eq 'Armor B') 'Migration altered live files.'

  Write-Host 'PASS: v7 responsive GUI backend, indexed conflict analysis, sparse multi-provider rules, typed smart conflicts, overlays/providers, layered restoration, recaptured unmanaged bases, profiles/export, coverage, support bundle, rollback, path-shape checks, committed recovery, history, and legacy migration.'
}finally{
  Remove-Item Env:MHW_MANAGER_TEST_GAME_ROOT -ErrorAction SilentlyContinue
  Remove-Item -LiteralPath $fixture -Recurse -Force -ErrorAction SilentlyContinue
}
