# Transactional deployment, crash recovery, history, and undo. v7 keeps deployments non-interactive for the GUI and avoids expensive automatic report rebuilds.
function New-OperationId { return ((Get-Date).ToUniversalTime().ToString('yyyyMMddTHHmmssfffZ')+'-'+[guid]::NewGuid().ToString('N').Substring(0,8)) }
function Finalize-History([string]$txn){
  $journalPath=Join-Path $txn 'journal.json'
  if(!(Test-Path -LiteralPath $journalPath -PathType Leaf)){return}
  $journal=Get-Content -LiteralPath $journalPath -Raw | ConvertFrom-Json
  $id=$journal.operationId
  if(!$id){$id=New-OperationId}
  $dest=Join-Path $HistoryRoot $id
  if(!(Test-Path -LiteralPath $dest -PathType Container)){
    New-Item -ItemType Directory -Force -Path $dest | Out-Null
    foreach($name in @('journal.json','before-state.json','after-state.json')){
      $src=Join-Path $txn $name
      if(Test-Path -LiteralPath $src -PathType Leaf){Copy-Item -LiteralPath $src -Destination (Join-Path $dest $name) -Force}
    }
  }
}
function Recover-Transaction {
  $txn=Join-Path $V2Root 'Pending'
  $journalPath=Join-Path $txn 'journal.json'
  if(!(Test-Path -LiteralPath $journalPath -PathType Leaf)){return}
  $journal=Get-Content -LiteralPath $journalPath -Raw | ConvertFrom-Json
  if(Test-Path -LiteralPath (Join-Path $txn 'COMMITTED') -PathType Leaf){
    Finalize-History $txn
    Remove-Item -LiteralPath $txn -Recurse -Force
    Write-Host 'Recovered a committed manager operation; no game files were rolled back.'
    return
  }
  foreach($item in @($journal.files)){
    $dest=Destination $item.key
    if($item.existed){
      New-Item -ItemType Directory -Force -Path (Split-Path -Parent $dest) | Out-Null
      Copy-Item -LiteralPath (Join-Path $txn $item.index) -Destination $dest -Force
    }elseif(Test-Path -LiteralPath $dest -PathType Leaf){Remove-Item -LiteralPath $dest -Force}
  }
  $before=Join-Path $txn 'before-state.json'
  if($journal.stateExisted -and (Test-Path -LiteralPath $before -PathType Leaf)){Copy-Item -LiteralPath $before -Destination $StateFile -Force}
  elseif(!$journal.stateExisted){Remove-Item -LiteralPath $StateFile -ErrorAction SilentlyContinue}
  Remove-Item -LiteralPath $txn -Recurse -Force
  Write-Host 'Recovered an interrupted manager operation by restoring its previous files and state.'
}
function Finalize-NextState($next,$plan){
  $newExpected=New-Map
  foreach($p in $plan.active.PSObject.Properties){Map-Set $newExpected $p.Name $p.Value.hash}
  $next.expected=$newExpected
  foreach($key in @($plan.release)){
    Map-Remove $next.bases $key
    Map-Remove $next.winners $key
  }
  # Remove any orphaned base references from older builds.
  $active=Get-ActiveKeys $next
  foreach($p in @($next.bases.PSObject.Properties)){if(!$active.ContainsKey($p.Name)){Map-Remove $next.bases $p.Name}}
  return $next
}
function Write-DryRunReport($plan,[string]$description){
  $path=Join-Path $ToolRoot 'Dry Run Report.txt'
  $lines=[Collections.Generic.List[string]]::new()
  $lines.Add('MHW Manual Mod Manager v7 - dry run')
  $lines.Add("Generated: $((Get-Date).ToString('o'))")
  $lines.Add("Plan: $description")
  $lines.Add("File changes: $($plan.changes.Count)")
  $lines.Add("Released unmanaged paths: $($plan.release.Count)")
  $lines.Add('')
  foreach($c in $plan.changes){$lines.Add("$($c.key) | from=$($c.from) | to=$($c.to) | winner=$($c.winner)")}
  $lines | Set-Content -LiteralPath $path -Encoding UTF8
  return $path
}
function Apply-State($old,$next,[switch]$DryRun,[string]$Description='Deployment'){
  Write-ManagerLog 'info' "Apply-State start: $Description"
  Assert-GameClosed
  Verify-Live $old
  $working=Clone-State $next
  if($DryRun){$plan=Build-DeploymentPlan $old $working}else{$plan=Build-DeploymentPlan $old $working -CaptureBases}
  Show-Plan $plan $Description
  if($DryRun){
    $path=Write-DryRunReport $plan $Description
    Write-Host "Dry run only. Saved $path"
    return $plan
  }
  $txn=Join-Path $V2Root 'Pending'
  Assert (!(Test-Path -LiteralPath $txn)) 'Pending transaction exists; restart manager to recover it.'
  New-Item -ItemType Directory -Path $txn | Out-Null
  $operationId=New-OperationId
  try{
    $stateExisted=Test-Path -LiteralPath $StateFile -PathType Leaf
    if($stateExisted){Copy-Item -LiteralPath $StateFile -Destination (Join-Path $txn 'before-state.json') -Force}
    else{Atomic-Json (Join-Path $txn 'before-state.json') $old}
    $journalFiles=[Collections.Generic.List[object]]::new()
    $journal=[ordered]@{operationId=$operationId;description=$Description;startedAt=(Get-Date).ToString('o');stateExisted=$stateExisted;files=$journalFiles}
    $i=0
    foreach($c in $plan.changes){
      $dest=Destination $c.key
      Assert (!(Test-Path -LiteralPath $dest -PathType Container)) "Destination is a folder: $dest"
      $existed=Test-Path -LiteralPath $dest -PathType Leaf
      $index=('file{0:D6}' -f $i);$i++
      if($existed){Copy-Item -LiteralPath $dest -Destination (Join-Path $txn $index) -Force}
      $journalFiles.Add([pscustomobject]@{key=$c.key;existed=$existed;index=$index})
    }
    Atomic-Json (Join-Path $txn 'journal.json') $journal
    foreach($c in $plan.changes){
      $dest=Destination $c.key
      if($c.to){
        $blob=Blob-Path $c.to
        Assert ((File-Hash $blob) -eq $c.to) "Missing/corrupt stored file for $($c.key)"
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $dest) | Out-Null
        Copy-Item -LiteralPath $blob -Destination $dest -Force
      }elseif(Test-Path -LiteralPath $dest -PathType Leaf){Remove-Item -LiteralPath $dest -Force}
    }
    $working=Finalize-NextState $working $plan
    Save-State $working
    Atomic-Json (Join-Path $txn 'after-state.json') $working
    Set-Content -LiteralPath (Join-Path $txn 'COMMITTED') -Value $operationId -Encoding ASCII
    Finalize-History $txn
    Remove-Item -LiteralPath $txn -Recurse -Force
  }catch{
    if(Test-Path -LiteralPath (Join-Path $txn 'COMMITTED') -PathType Leaf){
      $message=$_.Exception.Message
      try{Finalize-History $txn}catch{}
      Remove-Item -LiteralPath $txn -Recurse -Force -ErrorAction SilentlyContinue
      Write-Warning "Deployment committed, but history finalization/cleanup reported: $message"
      return $plan
    }
    Recover-Transaction
    throw
  }
  Write-ManagerLog 'info' "Apply-State committed: $Description" ([ordered]@{changes=$plan.changes.Count;released=$plan.release.Count})
  Write-Host "Applied $($plan.changes.Count) file change(s)."
  return $plan
}
function Commit-Plan($old,$next,[string]$description){
  Resolve-Choices $next
  $preview=Apply-State $old (Clone-State $next) -DryRun -Description $description
  if((Read-Host 'Type APPLY to make these changes') -cne 'APPLY'){Write-Host 'Cancelled.';return $false}
  Apply-State $old $next -Description $description | Out-Null
  return $true
}

function Commit-PlanAutomatic($old,$next,[string]$description){
  $blockers=@(Get-UnresolvedConflictGroups $next)
  if($blockers.Count){
    Write-ManagerLog 'warn' "Deployment blocked: $description" ([ordered]@{groups=$blockers.Count})
    return [pscustomobject]@{success=$false;blocked=$true;blockers=$blockers;plan=$null;message="$($blockers.Count) conflict decision(s) need attention."}
  }
  try{
    $preview=Apply-State $old (Clone-State $next) -DryRun -Description $description
    $applied=Apply-State $old $next -Description $description
    return [pscustomobject]@{success=$true;blocked=$false;blockers=@();plan=$applied;preview=$preview;message="Applied $($applied.changes.Count) file change(s)."}
  }catch{
    Write-ManagerLog 'error' "Deployment failed: $description" $_.Exception.ToString()
    throw
  }
}
function Get-DeploymentPreview($old,$next,[string]$description='Pending changes'){
  $blockers=@(Get-UnresolvedConflictGroups $next)
  if($blockers.Count){return [pscustomobject]@{blocked=$true;blockers=$blockers;plan=$null;description=$description}}
  $plan=Build-DeploymentPlan $old (Clone-State $next)
  return [pscustomobject]@{blocked=$false;blockers=@();plan=$plan;description=$description}
}
function Undo-LastDeploymentAutomatic($s){
  $items=@(Get-HistoryRecords 1);Assert $items.Count 'No deployment history to undo.'
  $item=$items[0];$beforePath=Join-Path $item.path 'before-state.json';Assert (Test-Path -LiteralPath $beforePath -PathType Leaf) "History is missing before-state.json for $($item.id)."
  $before=Ensure-StateShape (Get-Content -LiteralPath $beforePath -Raw | ConvertFrom-Json)
  $next=Clone-State $s;$next.mods=$before.mods;$next.order=@($before.order);$next.winners=$before.winners;$next.sharedPolicy=$before.sharedPolicy;$next.smartConflicts=$before.smartConflicts;$next.relations=$before.relations;$next.resourceProviders=$before.resourceProviders
  return (Commit-PlanAutomatic $s $next "Undo: $($item.description)")
}

function Get-HistoryRecords([int]$Limit=20){
  if(!(Test-Path -LiteralPath $HistoryRoot -PathType Container)){return @()}
  $out=@()
  foreach($dir in @(Get-ChildItem -LiteralPath $HistoryRoot -Directory | Sort-Object Name -Descending | Select-Object -First $Limit)){
    $metaPath=Join-Path $dir.FullName 'journal.json'
    if(!(Test-Path -LiteralPath $metaPath -PathType Leaf)){continue}
    try{
      $j=Get-Content -LiteralPath $metaPath -Raw | ConvertFrom-Json
      $out+=@([pscustomobject]@{id=$dir.Name;description=$j.description;startedAt=$j.startedAt;path=$dir.FullName})
    }catch{}
  }
  return @($out)
}
function Show-History([int]$Limit=12){
  $items=@(Get-HistoryRecords $Limit)
  if(!$items.Count){Write-Host 'No deployment history yet.';return}
  Write-Host 'Recent deployment history:'
  for($i=0;$i -lt $items.Count;$i++){Write-Host ('  {0,2}. {1} | {2}' -f ($i+1),$items[$i].startedAt,$items[$i].description)}
}
function Undo-LastDeployment($s){
  $items=@(Get-HistoryRecords 1)
  Assert $items.Count 'No deployment history to undo.'
  $item=$items[0]
  $beforePath=Join-Path $item.path 'before-state.json'
  Assert (Test-Path -LiteralPath $beforePath -PathType Leaf) "History is missing before-state.json for $($item.id)."
  $before=Ensure-StateShape (Get-Content -LiteralPath $beforePath -Raw | ConvertFrom-Json)
  $next=Clone-State $s
  $next.mods=$before.mods
  $next.order=@($before.order)
  $next.winners=$before.winners
  $next.sharedPolicy=$before.sharedPolicy
  $next.smartConflicts=$before.smartConflicts
  $next.relations=$before.relations
  $next.resourceProviders=$before.resourceProviders
  Commit-Plan $s $next "Undo: $($item.description)" | Out-Null
}
