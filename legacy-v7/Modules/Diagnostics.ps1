# Structured diagnostics and support bundle helpers for the v7 GUI.
function New-HealthIssue([string]$severity,[string]$category,[string]$message,[string]$mod='',[string]$path='',[string]$action=''){
  return [pscustomobject]@{Severity=$severity;Category=$category;Message=$message;Mod=$mod;Path=$path;Action=$action}
}
function Get-HealthIssues($s,[switch]$Fast){
  $issues=[Collections.Generic.List[object]]::new()
  if(!(Test-Path -LiteralPath $GameRoot -PathType Container)){$issues.Add((New-HealthIssue 'Error' 'Game' "Monster Hunter: World folder not found: $GameRoot" '' $GameRoot 'Choose the correct game folder.'));return @($issues)}
  if(!(Test-Path -LiteralPath (Join-Path $GameRoot 'MonsterHunterWorld.exe') -PathType Leaf)){$issues.Add((New-HealthIssue 'Warning' 'Game' 'MonsterHunterWorld.exe was not found in the configured game folder.' '' $GameRoot 'Verify the game folder.'))}
  if(!$Fast){
    foreach($p in $s.expected.PSObject.Properties){
      $actual=File-Hash (Destination $p.Name)
      if($actual -ne $p.Value){$issues.Add((New-HealthIssue 'Error' 'Deployment' 'Deployed file changed or is missing.' '' $p.Name 'Do not deploy until this is understood; use the support bundle if unexpected.'))}
    }
  }
  $candidateIndex=Get-CandidateIndex $s
  foreach($shape in @(Get-PathShapeConflicts $s $candidateIndex)){$issues.Add((New-HealthIssue 'Error' 'Layout' $shape '' '' 'Fix the colliding file/folder layout.'))}
  $blockers=@(Get-UnresolvedConflictGroups $s $candidateIndex)
  foreach($g in $blockers){$issues.Add((New-HealthIssue 'Action' 'Conflict' ("[$($g.kind)] $($g.reason)") ($g.names -join ' <> ') '' 'Resolve this once in the Conflicts page.'))}
  $pending=Join-Path $V2Root 'Pending';if(Test-Path -LiteralPath $pending -PathType Container){$issues.Add((New-HealthIssue 'Warning' 'Recovery' 'A pending transaction folder exists.' '' $pending 'Restart normally so automatic recovery can finish.'))}
  if(!$Fast){
    foreach($m in $s.mods.PSObject.Properties){
      $folder=Join-Path $ModsRoot $m.Name
      if(!(Test-Path -LiteralPath $folder -PathType Container)){$issues.Add((New-HealthIssue 'Error' 'Source' 'Enabled mod source folder is missing.' $m.Name $folder 'Restore the folder or disable the mod.'));continue}
      try{
        $files=Get-SourceFiles $m.Name
        foreach($p in $m.Value.files.PSObject.Properties){
          $source=Map-Get $files $p.Name
          if(!$source){$issues.Add((New-HealthIssue 'Warning' 'Source' 'Captured source file no longer exists.' $m.Name $p.Name 'Use Refresh captured version only if this edit was intentional.'))}
          elseif((File-Hash $source) -ne $p.Value){$issues.Add((New-HealthIssue 'Info' 'Source' 'Source folder differs from the captured enabled version.' $m.Name $p.Name 'This is safe until you choose Refresh captured version.'))}
          if((File-Hash (Blob-Path $p.Value)) -ne $p.Value){$issues.Add((New-HealthIssue 'Error' 'Snapshot' 'Stored snapshot is missing or corrupt.' $m.Name $p.Name 'Restore State/V2/Blobs from backup before changing mods.'))}
        }
      }catch{$issues.Add((New-HealthIssue 'Error' 'Layout' $_.Exception.Message $m.Name $folder 'Fix this mod folder layout.'))}
    }
    foreach($p in $s.bases.PSObject.Properties){if($p.Value -and (File-Hash (Blob-Path $p.Value)) -ne $p.Value){$issues.Add((New-HealthIssue 'Error' 'Backup' 'Original unmanaged backup is missing or corrupt.' '' $p.Name 'Restore the State folder from backup.'))}}
  }
  return @($issues)
}
function Get-QuickHealthIssues($s){
  $issues=[Collections.Generic.List[object]]::new()
  if(!(Test-Path -LiteralPath $GameRoot -PathType Container)){$issues.Add((New-HealthIssue 'Error' 'Game' "Monster Hunter: World folder not found: $GameRoot" '' $GameRoot 'Choose the correct game folder.'));return @($issues)}
  if(!(Test-Path -LiteralPath (Join-Path $GameRoot 'MonsterHunterWorld.exe') -PathType Leaf)){$issues.Add((New-HealthIssue 'Warning' 'Game' 'MonsterHunterWorld.exe was not found in the configured game folder.' '' $GameRoot 'Verify the game folder.'))}
  $pending=Join-Path $V2Root 'Pending';if(Test-Path -LiteralPath $pending -PathType Container){$issues.Add((New-HealthIssue 'Warning' 'Recovery' 'A pending transaction folder exists.' '' $pending 'Restart normally so automatic recovery can finish.'))}
  return @($issues)
}
function Get-ManagerSummary($s,$ConflictGroups=$null,[int]$Installed=-1){
  if($Installed -lt 0){$Installed=@(Get-ChildItem -LiteralPath $ModsRoot -Directory -ErrorAction SilentlyContinue).Count}
  $groups=if($null -ne $ConflictGroups){@($ConflictGroups)}else{@(Get-ConflictGroupsForUi $s)}
  $blocking=@($groups | Where-Object {$_.blocking}).Count
  $auto=@($groups | Where-Object {!$_.blocking}).Count
  $fastIssues=@(Get-QuickHealthIssues $s)
  return [pscustomobject]@{Installed=$Installed;Enabled=@($s.order).Count;Blocking=$blocking;AutoManaged=$auto;HealthIssues=$fastIssues.Count;GameRoot=$GameRoot}
}
function Export-SupportBundle($s){
  $supportRoot=Join-Path $ToolRoot 'Support';New-Item -ItemType Directory -Force -Path $supportRoot | Out-Null
  $stamp=(Get-Date).ToString('yyyyMMdd-HHmmss');$stage=Join-Path $supportRoot ('bundle-'+$stamp);New-Item -ItemType Directory -Path $stage | Out-Null
  try{
    $version=if(Test-Path -LiteralPath (Join-Path $ToolRoot 'VERSION.txt')){(Get-Content -LiteralPath (Join-Path $ToolRoot 'VERSION.txt') -Raw).Trim()}else{'unknown'}
    @("MHW Manual Mod Manager support bundle","Version: $version","Generated: $((Get-Date).ToString('o'))","Windows: $([Environment]::OSVersion.VersionString)","PowerShell: $($PSVersionTable.PSVersion)","Game root: $GameRoot","Enabled mods: $(@($s.order).Count)") | Set-Content -LiteralPath (Join-Path $stage 'environment.txt') -Encoding UTF8
    if(Test-Path -LiteralPath $StateFile -PathType Leaf){Copy-Item -LiteralPath $StateFile -Destination (Join-Path $stage 'state.json') -Force}
    if(Test-Path -LiteralPath $UiSettingsFile -PathType Leaf){Copy-Item -LiteralPath $UiSettingsFile -Destination (Join-Path $stage 'ui-settings.json') -Force}
    $draft=Join-Path $V2Root 'ui-draft.json';if(Test-Path -LiteralPath $draft -PathType Leaf){Copy-Item -LiteralPath $draft -Destination (Join-Path $stage 'ui-draft.json') -Force}
    $health=@(Get-HealthIssues $s);$health | Export-Csv -LiteralPath (Join-Path $stage 'health.csv') -NoTypeInformation -Encoding UTF8
    $conflicts=@(Get-ConflictGroupsForUi $s | ForEach-Object {[pscustomobject]@{Kind=$_.kind;Blocking=$_.blocking;Mods=($_.names -join ' <> ');Paths=$_.items.Count;Winner=$_.winner;Resolution=$_.resolution;Reason=$_.reason}})
    $conflicts | Export-Csv -LiteralPath (Join-Path $stage 'conflicts.csv') -NoTypeInformation -Encoding UTF8
    $history=@(Get-HistoryRecords 30 | Select-Object id,description,startedAt);$history | Export-Csv -LiteralPath (Join-Path $stage 'history.csv') -NoTypeInformation -Encoding UTF8
    if(Test-Path -LiteralPath $LogRoot -PathType Container){New-Item -ItemType Directory -Path (Join-Path $stage 'Logs') | Out-Null;Get-ChildItem -LiteralPath $LogRoot -File | Sort-Object LastWriteTime -Descending | Select-Object -First 5 | Copy-Item -Destination (Join-Path $stage 'Logs')}
    $zip=Join-Path $supportRoot ("MHW-Manager-Support-$stamp.zip");Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip -Force
    Write-ManagerLog 'info' 'Created support bundle' $zip
    return $zip
  }finally{if(Test-Path -LiteralPath $stage){Remove-Item -LiteralPath $stage -Recurse -Force}}
}
