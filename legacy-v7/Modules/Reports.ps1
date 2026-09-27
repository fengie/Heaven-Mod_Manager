# Conflict, health, armor coverage, and diagnostics reports.
function Conflict-Report($s){
  $lines=[Collections.Generic.List[string]]::new()
  $lines.Add('MHW Manual Mod Manager v7 - typed conflict report')
  $lines.Add("Generated: $((Get-Date).ToString('o'))")
  $lines.Add('IDENTICAL = same bytes. SHARED-TEXTURE/TEXTURE-OVERRIDE = soft visual/resource overlap. PATCH-OVERLAY = inferred base + option. HARD-* = requires an explicit decision.')
  $lines.Add('Enabled mods use captured snapshot hashes; current source hashes are shown separately so edited source folders do not rewrite history.')
  $lines.Add('')
  $byKey=@{}
  foreach($dir in @(Get-ChildItem -LiteralPath $ModsRoot -Directory | Sort-Object Name)){
    try{
      $files=Get-SourceFiles $dir.Name
      foreach($p in $files.PSObject.Properties){
        if(!$byKey.ContainsKey($p.Name)){$byKey[$p.Name]=@()}
        $on=Map-Has $s.mods $dir.Name;$sourceHash=File-Hash $p.Value;$captured=$null
        if($on){$mod=Map-Get $s.mods $dir.Name;if(Map-Has $mod.files $p.Name){$captured=Map-Get $mod.files $p.Name}}
        $effective=if($captured){$captured}else{$sourceHash}
        $byKey[$p.Name]+=[pscustomobject]@{name=$dir.Name;on=$on;source=$sourceHash;captured=$captured;effective=$effective}
      }
    }catch{$lines.Add("Skipped $($dir.Name): $($_.Exception.Message)")}
  }
  foreach($modProp in $s.mods.PSObject.Properties){
    foreach($p in $modProp.Value.files.PSObject.Properties){
      if(!$byKey.ContainsKey($p.Name)){$byKey[$p.Name]=@()}
      if(!@($byKey[$p.Name] | Where-Object {$_.name -eq $modProp.Name}).Count){$byKey[$p.Name]+=[pscustomobject]@{name=$modProp.Name;on=$true;source=$null;captured=$p.Value;effective=$p.Value}}
    }
  }
  $counts=@{};$count=0
  foreach($key in @($byKey.Keys | Sort-Object)){
    $c=@($byKey[$key]);if($c.Count -lt 2){continue};$count++
    $active=@($c | Where-Object {$_.on} | ForEach-Object {[pscustomobject]@{name=$_.name;hash=$_.effective}})
    if($active.Count -ge 2){$a=Analyze-Conflict $s $key $active -IgnoreExplicit}else{$a=[pscustomobject]@{kind='POTENTIAL';blocking=$false;winner=$null;reason='fewer than two providers are enabled'}}
    $saved=Map-Get $s.winners $key
    $resolved=($saved -and @($active.name) -contains $saved)
    $displayWinner=if($resolved){$saved}else{$a.winner}
    if(!$counts.ContainsKey($a.kind)){$counts[$a.kind]=0};$counts[$a.kind]++
    $flag=if($a.blocking -and !$resolved){'ACTION REQUIRED'}elseif($a.blocking){'RESOLVED'}else{'OK'}
    $lines.Add("$key [$($a.kind)] $flag WINNER: $(if($displayWinner){$displayWinner}else{'none'})")
    $lines.Add("  $($a.reason)")
    foreach($item in $c){$changed=if($item.on -and $item.captured -and $item.source -ne $item.captured){' SOURCE-CHANGED'}else{''};$lines.Add("  $(if($item.on){'ON '}else{'OFF'}) $($item.name) effective=$($item.effective) captured=$($item.captured) source=$($item.source)$changed")}
  }
  $summary='Kinds: '+(@($counts.Keys | Sort-Object | ForEach-Object {"$_=$($counts[$_])"}) -join ', ')
  $lines.Insert(4,$summary)
  $path=Join-Path $ToolRoot 'Conflict Report.txt';$lines | Set-Content -LiteralPath $path -Encoding UTF8
  Write-Host "Saved $path ($count overlapping path(s)); $summary";return $path
}
function Health-Report($s){
  $issues=[Collections.Generic.List[string]]::new()
  foreach($dir in @(Get-ChildItem -LiteralPath $ModsRoot -Directory | Sort-Object Name)){
    try{Get-SourceFiles $dir.Name | Out-Null}catch{$issues.Add("Layout: $($dir.Name): $($_.Exception.Message)")}
    foreach($doc in @(Get-ChildItem -LiteralPath $dir.FullName -Recurse -File -ErrorAction SilentlyContinue | Where-Object {$_.Name -match '(?i)readme|install|require'} | Select-Object -First 8)){
      $body=Get-Content -LiteralPath $doc.FullName -Raw -ErrorAction SilentlyContinue
      if($body -match '(?i)stracker.?s loader'){$issues.Add("Check requirement: $($dir.Name) mentions Stracker's Loader; verify its game-root files manually.")}
      if($body -match '(?i)sharppluginloader' -and !(Test-Path -LiteralPath (Join-Path $GameRoot 'winmm.dll'))){$issues.Add("Check requirement: $($dir.Name) mentions SharpPluginLoader; verify its game-root files manually.")}
      if($body -match '(?i)performance booster' ){$issues.Add("Check requirement: $($dir.Name) mentions Performance Booster; verify dependencies manually.")}
    }
  }
  foreach($p in $s.expected.PSObject.Properties){
    $actual=File-Hash (Destination $p.Name)
    if($actual -ne $p.Value){$issues.Add("Changed/missing deployment: $($p.Name) expected=$($p.Value) actual=$actual")}
  }
  foreach($m in $s.mods.PSObject.Properties){
    if(!(Test-Path -LiteralPath (Join-Path $ModsRoot $m.Name) -PathType Container)){$issues.Add("Missing source folder: $($m.Name)");continue}
    try{
      $files=Get-SourceFiles $m.Name
      foreach($p in $m.Value.files.PSObject.Properties){
        $source=Map-Get $files $p.Name
        if(!$source){$issues.Add("Missing source file: $($m.Name) $($p.Name)")}
        elseif((File-Hash $source) -ne $p.Value){$issues.Add("Changed source file: $($m.Name) $($p.Name) (use Update if intentional)")}
        if((File-Hash (Blob-Path $p.Value)) -ne $p.Value){$issues.Add("Missing/corrupt snapshot: $($m.Name) $($p.Name)")}
      }
    }catch{$issues.Add("Bad layout $($m.Name): $($_.Exception.Message)")}
  }
  foreach($p in $s.bases.PSObject.Properties){if($p.Value -and (File-Hash (Blob-Path $p.Value)) -ne $p.Value){$issues.Add("Missing/corrupt original backup: $($p.Name)")}}
  foreach($shape in @(Get-PathShapeConflicts $s)){$issues.Add($shape)}
  try{Get-DesiredActive $s | Out-Null}catch{$issues.Add("Unresolved priority: $($_.Exception.Message)")}
  $pending=Join-Path $V2Root 'Pending'
  if(Test-Path -LiteralPath $pending -PathType Container){$issues.Add('A Pending transaction folder exists. Restart the manager normally so recovery can finish before manual cleanup.')}
  $unique=@($issues | Select-Object -Unique)
  $path=Join-Path $ToolRoot 'Health Report.txt'
  @("MHW Manual Mod Manager v7 health: $($unique.Count) issue(s)","Generated: $((Get-Date).ToString('o'))",'')+@($unique) | Set-Content -LiteralPath $path -Encoding UTF8
  Write-Host "Saved $path ($($unique.Count) issues)"
  foreach($line in @($unique | Select-Object -First 18)){Write-Host "  $line"}
  return $path
}
function Add-CoverageEntry($index,[string]$modelKey,[string]$name,[bool]$available,[bool]$enabled,[bool]$winning){
  if(!$index.ContainsKey($modelKey)){$index[$modelKey]=@{}}
  if(!$index[$modelKey].ContainsKey($name)){$index[$modelKey][$name]=[pscustomobject]@{name=$name;available=$false;enabled=$false;winning=$false}}
  $e=$index[$modelKey][$name]
  if($available){$e.available=$true};if($enabled){$e.enabled=$true};if($winning){$e.winning=$true}
}
function Get-CoverageIndex($s,$candidateIndex=$null){
  if($null -eq $candidateIndex){$candidateIndex=Get-CandidateIndex $s}
  $pairCache=@{};$byId=@{}
  foreach($dir in @(Get-ChildItem -LiteralPath $ModsRoot -Directory | Sort-Object Name)){
    try{
      $files=Get-SourceFiles $dir.Name
      foreach($p in $files.PSObject.Properties){
        if($p.Name -notmatch '(?i)^nativePC\\pl\\([fm])_equip\\(pl\d{3}_\d{4})\\(helm|body|arm|wst|leg)\\'){continue}
        $sex=$Matches[1].ToUpperInvariant();$id=$Matches[2].ToLowerInvariant();$slot=$Matches[3].ToLowerInvariant()
        Add-CoverageEntry $byId "$sex/$id/$slot" $dir.Name $true (Map-Has $s.mods $dir.Name) $false
      }
    }catch{}
  }
  foreach($modProp in $s.mods.PSObject.Properties){
    foreach($p in $modProp.Value.files.PSObject.Properties){
      if($p.Name -notmatch '(?i)^nativePC\\pl\\([fm])_equip\\(pl\d{3}_\d{4})\\(helm|body|arm|wst|leg)\\'){continue}
      $sex=$Matches[1].ToUpperInvariant();$id=$Matches[2].ToLowerInvariant();$slot=$Matches[3].ToLowerInvariant()
      $winning=$false
      $c=@(Get-Candidates $s $p.Name $candidateIndex)
      if($c.Count){
        $analysis=Analyze-Conflict $s $p.Name $c -PairCache $pairCache
        if(!$analysis.blocking){$winning=($analysis.winner -eq $modProp.Name)}
        elseif(Map-Has $s.winners $p.Name){$winning=((Map-Get $s.winners $p.Name) -eq $modProp.Name)}
      }
      Add-CoverageEntry $byId "$sex/$id/$slot" $modProp.Name $false $true $winning
    }
  }
  return $byId
}
function Write-CoveragePage($s,[bool]$Open=$true){
  $databasePath=Join-Path $ToolRoot 'Armor Database.csv'
  Assert (Test-Path -LiteralPath $databasePath -PathType Leaf) 'Armor Database.csv is missing.'
  $candidateIndex=Get-CandidateIndex $s
  $byId=Get-CoverageIndex $s $candidateIndex
  $armor=Import-Csv -LiteralPath $databasePath
  $html=[Collections.Generic.List[string]]::new()
  $html.Add('<!doctype html><html lang="en"><meta charset="utf-8"><title>MHW armor coverage</title><style>body{background:#171a20;color:#eee;font:15px system-ui;margin:25px}table{border-collapse:collapse;width:100%}th,td{border-bottom:1px solid #444;padding:8px;text-align:left;vertical-align:top}th{position:sticky;top:0;background:#242a33}.yes{background:#226645}.other{color:#dfbb6a}.no{color:#888}.small{font-size:12px;color:#c6ccd6}input,select{background:#29303a;color:white;padding:10px;margin:10px;border:1px solid #888}</style>')
  $html.Add('<h1>MHW armor coverage</h1><p>Green = active winning mod for that piece; gold = local mod available but not winning; &mdash; = no local mod. Shared model IDs intentionally repeat across game armor entries.</p>')
  $html.Add('<input id="q" placeholder="Search armor, model, mod"><select id="f"><option value="all">All</option><option value="active">Active</option><option value="available">Available</option><option value="none">No mod</option></select>')
  $html.Add('<table><thead><tr><th>Armor</th><th>Series</th><th>Sex</th><th>Model</th><th>Head</th><th>Chest</th><th>Arms</th><th>Waist</th><th>Legs</th><th>Local mods</th></tr></thead><tbody>')
  $known=@{}
  foreach($row in $armor){
    if($row.name -in @('Unavailable','HARDUMMY')){continue}
    $id=$row.model_id.ToLowerInvariant();$known[$id]=$true
    foreach($sex in @('F','M')){
      if(($row.name -eq 'King Beetle' -and $sex -eq 'F') -or ($row.name -eq 'Butterfly' -and $sex -eq 'M')){continue}
      $modsForRow=@();$cells=@();$active=$false;$available=$false
      foreach($slot in @('helm','body','arm','wst','leg')){
        $modelKey="$sex/$id/$slot"
        $entries=if($byId.ContainsKey($modelKey)){@($byId[$modelKey].Values)}else{@()}
        $wins=@($entries | Where-Object {$_.winning})
        if($wins.Count){
          $label=@($wins.name | Sort-Object -Unique | ForEach-Object {[System.Net.WebUtility]::HtmlEncode($_)}) -join '<br>'
          $cells+='<td class="yes">&#10003;<div class="small">'+$label+'</div></td>';$active=$true
        }elseif($entries.Count){$cells+='<td class="other">&#9675;</td>';$available=$true}else{$cells+='<td class="no">&mdash;</td>'}
        $modsForRow+=@($entries.name)
      }
      $status=if($active){'active'}elseif($available){'available'}else{'none'}
      $names=(@($modsForRow | Select-Object -Unique | Sort-Object) -join ', ')
      $html.Add('<tr data-status="'+$status+'"><td>'+[System.Net.WebUtility]::HtmlEncode($row.name)+'</td><td>'+$row.series_id+'</td><td>'+$sex+'</td><td>'+$id+'</td>'+($cells -join '')+'<td>'+[System.Net.WebUtility]::HtmlEncode($names)+'</td></tr>')
    }
  }
  foreach($id in @($byId.Keys | ForEach-Object {($_ -split '/')[1]} | Select-Object -Unique | Where-Object {!$known.ContainsKey($_)} | Sort-Object)){$html.Add('<tr data-status="available"><td>Custom / unlisted model</td><td>&mdash;</td><td>&mdash;</td><td>'+$id+'</td><td colspan="6">See local mod folders</td></tr>')}
  $html.Add('</tbody></table><script>const q=document.getElementById("q"),f=document.getElementById("f");function filter(){document.querySelectorAll("tbody tr").forEach(r=>r.hidden=!r.textContent.toLowerCase().includes(q.value.toLowerCase())||(f.value!=="all"&&f.value!==r.dataset.status))}q.oninput=filter;f.onchange=filter</script></html>')
  $path=Join-Path $ToolRoot 'Outfit Coverage.html'
  $html | Set-Content -LiteralPath $path -Encoding UTF8
  if($Open){Start-Process $path}
  return $path
}
function Coverage-Page($s){return (Write-CoveragePage $s $true)}

# Structured armor rows used by the v7 desktop UI.
function Get-CoverageRowsForUi($s){
  $databasePath=Join-Path $ToolRoot 'Armor Database.csv';Assert (Test-Path -LiteralPath $databasePath -PathType Leaf) 'Armor Database.csv is missing.'
  $candidateIndex=Get-CandidateIndex $s;$byId=Get-CoverageIndex $s $candidateIndex;$armor=Import-Csv -LiteralPath $databasePath;$rows=@()
  foreach($row in $armor){
    if($row.name -in @('Unavailable','HARDUMMY')){continue}
    $id=$row.model_id.ToLowerInvariant()
    foreach($sex in @('F','M')){
      if(($row.name -eq 'King Beetle' -and $sex -eq 'F') -or ($row.name -eq 'Butterfly' -and $sex -eq 'M')){continue}
      $slotValues=@{};$hasActive=$false;$hasAvailable=$false;$mods=@()
      foreach($pair in @(@('Head','helm'),@('Chest','body'),@('Arms','arm'),@('Waist','wst'),@('Legs','leg'))){
        $key="$sex/$id/$($pair[1])";$entries=if($byId.ContainsKey($key)){@($byId[$key].Values)}else{@()};$wins=@($entries|Where-Object {$_.winning})
        if($wins.Count){$slotValues[$pair[0]]='ON: '+(@($wins.name|Sort-Object -Unique)-join ', ');$hasActive=$true}
        elseif($entries.Count){$slotValues[$pair[0]]='Available';$hasAvailable=$true}else{$slotValues[$pair[0]]='—'}
        $mods+=@($entries.name)
      }
      $status=if($hasActive){'Active'}elseif($hasAvailable){'Available'}else{'Unmodded'}
      $rows+=@([pscustomobject]@{Armor=$row.name;Series=$row.series_id;Sex=$sex;Model=$id;Status=$status;Head=$slotValues.Head;Chest=$slotValues.Chest;Arms=$slotValues.Arms;Waist=$slotValues.Waist;Legs=$slotValues.Legs;Mods=(@($mods|Select-Object -Unique|Sort-Object)-join ', ')})
    }
  }
  return @($rows)
}
