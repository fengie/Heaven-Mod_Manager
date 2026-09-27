# Planning and smart conflict resolution.
function Is-ModHepsyPath([string]$key){ return ($key -match '(?i)\\mod_hepsy\\') }
function Get-FileClass([string]$key){
  $ext=[IO.Path]::GetExtension($key).ToLowerInvariant()
  if($ext -eq '.tex'){return 'texture'}
  if($ext -in @('.mod3','.mrl3','.ctc','.ccl','.evbd','.evhl')){return 'structural'}
  if($ext -in @('.lmt','.timl','.efx','.epv3','.gmd','.sobj','.em','.col')){return 'game-data'}
  return 'other'
}
function Clean-RelationName([string]$name){
  $n=($name -replace '-\d{2,7}-\d+(?:-\d+){0,5}(?:\s*\(\d+\))?$','').Trim().ToLowerInvariant()
  $n=$n.Replace([string][char]0x2013,'-').Replace([string][char]0x2014,'-')
  return (($n -replace '\s+',' ').Trim())
}
function Get-RelationKey([string]$a,[string]$b){
  $pair=@($a,$b) | Sort-Object
  return [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes(($pair -join "`n")))
}
function Get-ModRelation($s,[string]$a,[string]$b){ return (Map-Get $s.relations (Get-RelationKey $a $b)) }
function Set-ModRelationMemory($s,[string]$a,[string]$b,[string]$mode,[string]$winner){
  Assert ($a -and $b -and $a -ne $b) 'Choose two different mods.'
  $key=Get-RelationKey $a $b
  if($mode -eq 'clear'){Map-Remove $s.relations $key;return}
  Assert ($mode -in @('overlay','incompatible')) 'Relationship must be OVERLAY or INCOMPATIBLE.'
  if($mode -eq 'overlay'){Assert ($winner -in @($a,$b)) 'Overlay winner must be one of the two mods.'}
  else{$winner=$null}
  Map-Set $s.relations $key ([pscustomobject]@{a=$a;b=$b;mode=$mode;winner=$winner;updatedAt=(Get-Date).ToString('o')})
}
function Get-ResourceNamespace([string]$key){
  if($key -match '(?i)^(nativePC\\.*?\\mod_[^\\]+)\\'){return $Matches[1].ToLowerInvariant()}
  return $null
}
function Get-ProviderWinner($s,[string]$key,$candidates){
  $ns=Get-ResourceNamespace $key
  if(!$ns){return $null}
  $provider=Map-Get $s.resourceProviders $ns
  if($provider -and @($candidates.name) -contains $provider){return $provider}
  return $null
}
function Test-LikelyChildName([string]$child,[string]$base){
  $c=Clean-RelationName $child;$b=Clean-RelationName $base
  if(!$c -or !$b -or $c.Length -le $b.Length){return $false}
  return $c.StartsWith($b+' - ')
}
function Get-AutoOverlayWinner($s,$candidates){
  $c=@($candidates)
  if($c.Count -ne 2){return $null}
  $rel=Get-ModRelation $s $c[0].name $c[1].name
  if($rel -and $rel.mode -eq 'overlay' -and @($c.name) -contains $rel.winner){return $rel.winner}
  if(Test-LikelyChildName $c[0].name $c[1].name){return $c[0].name}
  if(Test-LikelyChildName $c[1].name $c[0].name){return $c[1].name}
  return $null
}
function Get-IncompatiblePair($s,$candidates){
  # Saved relations are normally sparse. Scanning those is dramatically cheaper than
  # comparing every candidate pair on shared resources with dozens of providers.
  $present=@{}
  foreach($candidate in @($candidates)){$present[[string]$candidate.name]=$true}
  foreach($prop in @($s.relations.PSObject.Properties)){
    $rel=$prop.Value
    if($rel -and $rel.mode -eq 'incompatible' -and $present.ContainsKey([string]$rel.a) -and $present.ContainsKey([string]$rel.b)){return $rel}
  }
  return $null
}

function Get-NameTokens([string]$name){
  $stop=@('hpn','sexy','armor','armour','mod','main','file','files','alpha','beta','gamma','mr','hr','ex','the','and','for','player','version','ver')
  return @((Clean-RelationName $name -split '[^a-z0-9]+' | Where-Object {$_.Length -ge 4 -and $_ -notin $stop}) | Select-Object -Unique)
}
function Get-HighConfidenceSubsetOverlay($s,$candidates){
  $c=@($candidates);if($c.Count -ne 2){return $null}
  $m0=Map-Get $s.mods $c[0].name;$m1=Map-Get $s.mods $c[1].name;if(!$m0 -or !$m1){return $null}
  $n0=@($m0.files.PSObject.Properties).Count;$n1=@($m1.files.PSObject.Properties).Count;if(!$n0 -or !$n1 -or $n0 -eq $n1){return $null}
  $smallName=if($n0 -lt $n1){$c[0].name}else{$c[1].name};$bigName=if($n0 -lt $n1){$c[1].name}else{$c[0].name}
  $small=Map-Get $s.mods $smallName;$big=Map-Get $s.mods $bigName;$smallCount=@($small.files.PSObject.Properties).Count;$bigCount=@($big.files.PSObject.Properties).Count
  if($smallCount -gt 12 -or $bigCount -lt ($smallCount+4)){return $null}
  $overlap=0;foreach($p in $small.files.PSObject.Properties){if(Map-Has $big.files $p.Name){$overlap++}}
  if($smallCount -eq 0 -or ($overlap/[double]$smallCount) -lt 0.90){return $null}
  $sn=(Clean-RelationName $smallName);$patchWord=($sn -match '(?i)(^|[ _-])(patch|fix|hotfix|optional|option)([ _-]|$)')
  if(!$patchWord){return $null}
  $smallTokens=@(Get-NameTokens $smallName);$bigTokens=@(Get-NameTokens $bigName)
  $shared=@($smallTokens | Where-Object {$bigTokens -contains $_})
  if(!$shared.Count){return $null}
  return [pscustomobject]@{winner=$smallName;small=$smallCount;big=$bigCount;overlap=$overlap;sharedTokens=$shared}
}

function Get-PossibleOverlaySuggestion($s,$candidates){
  $c=@($candidates);if($c.Count -ne 2){return $null}
  $m0=Map-Get $s.mods $c[0].name;$m1=Map-Get $s.mods $c[1].name
  if(!$m0 -or !$m1){return $null}
  $n0=@($m0.files.PSObject.Properties).Count;$n1=@($m1.files.PSObject.Properties).Count
  if(!$n0 -or !$n1 -or $n0 -eq $n1){return $null}
  $smallName=if($n0 -lt $n1){$c[0].name}else{$c[1].name};$bigName=if($n0 -lt $n1){$c[1].name}else{$c[0].name}
  $small=Map-Get $s.mods $smallName;$big=Map-Get $s.mods $bigName;$smallCount=@($small.files.PSObject.Properties).Count;$bigCount=@($big.files.PSObject.Properties).Count
  if($smallCount -gt 16 -or $bigCount -lt ($smallCount+4)){return $null}
  $overlap=0;foreach($p in $small.files.PSObject.Properties){if(Map-Has $big.files $p.Name){$overlap++}}
  if($smallCount -gt 0 -and ($overlap / [double]$smallCount) -ge 0.80){return [pscustomobject]@{winner=$smallName;small=$smallCount;big=$bigCount;overlap=$overlap}}
  return $null
}
function Get-PairHints($s,$candidates,[hashtable]$PairCache=$null){
  $c=@($candidates)
  if($c.Count -ne 2){return [pscustomobject]@{bad=(Get-IncompatiblePair $s $c);overlay=$null;subset=$null;possible=$null;relation=$null;deepReady=$true}}
  $n0=[string]$c[0].name;$n1=[string]$c[1].name
  $cacheKey=if([string]::CompareOrdinal($n0,$n1) -le 0){$n0+"`0"+$n1}else{$n1+"`0"+$n0}
  if($null -ne $PairCache -and $PairCache.ContainsKey($cacheKey)){return $PairCache[$cacheKey]}
  $rel=Get-ModRelation $s $n0 $n1
  $bad=if($rel -and $rel.mode -eq 'incompatible'){$rel}else{$null}
  $overlay=$null
  if($rel -and $rel.mode -eq 'overlay' -and @($c.name) -contains $rel.winner){$overlay=$rel.winner}
  elseif(Test-LikelyChildName $c[0].name $c[1].name){$overlay=$c[0].name}
  elseif(Test-LikelyChildName $c[1].name $c[0].name){$overlay=$c[1].name}
  $h=[pscustomobject]@{bad=$bad;overlay=$overlay;subset=$null;possible=$null;relation=$rel;deepReady=$false}
  if($null -ne $PairCache){$PairCache[$cacheKey]=$h}
  return $h
}
function Ensure-DeepPairHints($s,$candidates,$h){
  if($h.deepReady){return $h}
  $c=@($candidates)
  if($c.Count -eq 2){
    if($s.smartConflicts -eq 'on'){$h.subset=Get-HighConfidenceSubsetOverlay $s $c}
    $h.possible=Get-PossibleOverlaySuggestion $s $c
  }
  $h.deepReady=$true
  return $h
}
function Analyze-Conflict($s,[string]$key,$candidates,[switch]$IgnoreExplicit,[hashtable]$PairCache=$null){
  $c=@($candidates)
  if($c.Count -le 1){$singleWinner=$null;if($c.Count){$singleWinner=$c[0].name};return [pscustomobject]@{kind='NONE';blocking=$false;winner=$singleWinner;reason='single provider'}}
  $h=Get-PairHints $s $c $PairCache
  if($h.bad){return [pscustomobject]@{kind='INCOMPATIBLE';blocking=$true;winner=$null;reason=("{0} and {1} are marked incompatible" -f $h.bad.a,$h.bad.b)}}
  $explicit=Map-Get $s.winners $key
  if(!$IgnoreExplicit -and $explicit -and @($c.name) -contains $explicit){return [pscustomobject]@{kind='EXPLICIT';blocking=$false;winner=$explicit;reason='saved file winner'}}
  $allSame=$true;$firstHash=$c[0].hash
  for($i=1;$i -lt $c.Count;$i++){if($c[$i].hash -ne $firstHash){$allSame=$false;break}}
  if($allSame){
    if((Is-ModHepsyPath $key) -and $s.sharedPolicy -eq 'explicit'){return [pscustomobject]@{kind='IDENTICAL-OWNER';blocking=$true;winner=$null;reason='identical bytes, explicit ownership requested'}}
    return [pscustomobject]@{kind='IDENTICAL';blocking=$false;winner=$c[0].name;reason='byte-identical shared file'}
  }
  $provider=Get-ProviderWinner $s $key $c
  if($provider){return [pscustomobject]@{kind='SHARED-PROVIDER';blocking=$false;winner=$provider;reason='pinned shared-resource provider'}}
  if($h.overlay){
    $kind=if($h.relation -and $h.relation.mode -eq 'overlay'){'OVERLAY-RULE'}else{'PATCH-OVERLAY'}
    return [pscustomobject]@{kind=$kind;blocking=$false;winner=$h.overlay;reason='base/option overlay; both mods stay enabled'}
  }
  $class=Get-FileClass $key
  if($s.smartConflicts -eq 'on' -or $class -eq 'structural'){$h=Ensure-DeepPairHints $s $c $h}
  if($h.subset){return [pscustomobject]@{kind='PATCH-SUBSET';blocking=$false;winner=$h.subset.winner;reason=("high-confidence patch/fix subset ($($h.subset.overlap)/$($h.subset.small) files overlap); both mods stay enabled")}}
  if($s.smartConflicts -eq 'on' -and $class -eq 'texture'){
    $textureKind=if(Is-ModHepsyPath $key){'SHARED-TEXTURE'}else{'TEXTURE-OVERRIDE'}
    return [pscustomobject]@{kind=$textureKind;blocking=$false;winner=$c[-1].name;reason='soft texture overlap; highest-priority enabled mod wins this file'}
  }
  if($h.possible -and $class -eq 'structural'){
    return [pscustomobject]@{kind='POSSIBLE-OVERLAY';blocking=$true;winner=$null;reason=("$($h.possible.winner) is a small subset ($($h.possible.overlap)/$($h.possible.small) files overlap a $($h.possible.big)-file mod). If it is an intended patch/option, remember it as an overlay; otherwise keep this blocked.")}
  }
  if($class -eq 'structural'){return [pscustomobject]@{kind='HARD-STRUCTURAL';blocking=$true;winner=$null;reason='different model/material/physics data at the same path'}}
  if($class -eq 'game-data'){return [pscustomobject]@{kind='HARD-GAME-DATA';blocking=$true;winner=$null;reason='different game-data files at the same path'}}
  return [pscustomobject]@{kind='HARD-UNKNOWN';blocking=$true;winner=$null;reason='different non-texture files at the same path'}
}
function Needs-ExplicitWinner($s,[string]$key,$candidates){ return (Analyze-Conflict $s $key $candidates).blocking }
function Get-CandidateIndex($s){
  $index=@{}
  foreach($name in @($s.order)){
    $m=Map-Get $s.mods $name;if(!$m){continue}
    foreach($p in $m.files.PSObject.Properties){
      if(!$index.ContainsKey($p.Name)){$index[$p.Name]=[Collections.Generic.List[object]]::new()}
      $index[$p.Name].Add([pscustomobject]@{name=$name;hash=$p.Value})
    }
  }
  return $index
}
function Get-ActiveKeys($s,$candidateIndex=$null){
  if($null -ne $candidateIndex){$keys=@{};foreach($key in $candidateIndex.Keys){$keys[$key]=$true};return $keys}
  $keys=@{}
  foreach($name in @($s.order)){
    $m=Map-Get $s.mods $name;if(!$m){continue}
    foreach($p in $m.files.PSObject.Properties){$keys[$p.Name]=$true}
  }
  return $keys
}
function Get-DesiredActive($s,$candidateIndex=$null){
  if($null -eq $candidateIndex){$candidateIndex=Get-CandidateIndex $s}
  $out=New-Map;$pairCache=@{}
  foreach($key in @($candidateIndex.Keys)){
    $c=@($candidateIndex[$key]);Assert $c.Count "No active candidate for $key"
    $a=Analyze-Conflict $s $key $c -PairCache $pairCache
    if($a.blocking){throw "[$($a.kind)] $($a.reason): $key. Choose a winner, mark the pair as an intentional overlay, or disable one mod."}
    $chosen=$null
    foreach($candidate in $c){if($candidate.name -eq $a.winner){$chosen=$candidate;break}}
    if($null -eq $chosen){$chosen=$c[0]}
    Map-Set $out $key $chosen
  }
  return $out
}
function Get-UnresolvedConflictGroups($s,$candidateIndex=$null){
  if($null -eq $candidateIndex){$candidateIndex=Get-CandidateIndex $s}
  $groups=@{};$pairCache=@{}
  foreach($key in @($candidateIndex.Keys)){
    $c=@($candidateIndex[$key]);if($c.Count -lt 2){continue}
    $a=Analyze-Conflict $s $key $c -PairCache $pairCache
    if(!$a.blocking){continue}
    $names=@($c.name)
    $signature=$a.kind+'|'+(($names | ForEach-Object {$_.Length.ToString()+':'+$_}) -join ';')
    if(!$groups.ContainsKey($signature)){$groups[$signature]=[pscustomobject]@{kind=$a.kind;reason=$a.reason;names=$names;items=[Collections.Generic.List[object]]::new()}}
    $groups[$signature].items.Add([pscustomobject]@{key=$key;candidates=$c})
  }
  return @($groups.Values | Sort-Object @{Expression={$_.kind}},@{Expression={$_.names -join ' | '}})
}
function Resolve-Choices($s){
  $groups=@(Get-UnresolvedConflictGroups $s)
  if(!$groups.Count){return}
  $incompatible=@($groups | Where-Object {$_.kind -eq 'INCOMPATIBLE'})
  if($incompatible.Count){$g=$incompatible[0];throw "Mods marked INCOMPATIBLE are both enabled: $($g.names -join ' <> '). Disable one before deployment."}
  $total=0;foreach($g in $groups){$total+=$g.items.Count}
  Write-Host "$total high-risk overlapping path(s) need $($groups.Count) decision(s)."
  Write-Host 'Texture/resource overlaps and recognized base+option overlays are handled automatically and do not appear here.'
  foreach($group in $groups){
    $options=@($group.names)
    Write-Host '';Write-Host "[$($group.kind)] $($group.items.Count) path(s) - $($group.reason)"
    for($i=0;$i -lt $options.Count;$i++){Write-Host ('  [{0}] {1}' -f ($i+1),$options[$i])}
    foreach($item in @($group.items | Select-Object -First 7)){Write-Host "     $($item.key)"}
    if($group.items.Count -gt 7){Write-Host "     ... and $($group.items.Count-7) more path(s)"}
    if($options.Count -eq 2){Write-Host '  O1/O2 = remember that mod as an intentional overlay over the other (both stay ON)'}
    $pick=(Read-Host 'Winner number/name, O1/O2 overlay rule, EACH for per-file, or blank to cancel').Trim();Assert $pick 'Conflict resolution cancelled.'
    if($options.Count -eq 2 -and $pick -match '^(?i)O([12])$'){
      $winner=$options[[int]$Matches[1]-1]
      Set-ModRelationMemory $s $options[0] $options[1] 'overlay' $winner
      foreach($item in $group.items){Map-Set $s.winners $item.key $winner}
      Write-Host "Remembered overlay: $winner wins overlaps with $($options | Where-Object {$_ -ne $winner})."
      continue
    }
    if($pick -ieq 'EACH'){
      foreach($item in $group.items){
        $itemOptions=@($item.candidates.name);Write-Host $item.key
        for($i=0;$i -lt $itemOptions.Count;$i++){Write-Host ('  [{0}] {1}' -f ($i+1),$itemOptions[$i])}
        $one=(Read-Host 'Winner number/name (blank cancels)').Trim();Assert $one 'Conflict resolution cancelled.'
        if($one -match '^\d+$' -and [int]$one -ge 1 -and [int]$one -le $itemOptions.Count){$winner=$itemOptions[[int]$one-1]}else{$winner=$one}
        Assert ($itemOptions -contains $winner) "Not a candidate for $($item.key): $winner";Map-Set $s.winners $item.key $winner
      }
      continue
    }
    if($pick -match '^\d+$' -and [int]$pick -ge 1 -and [int]$pick -le $options.Count){$winner=$options[[int]$pick-1]}else{$winner=$pick}
    Assert ($options -contains $winner) "Not a candidate for this conflict group: $winner"
    foreach($item in $group.items){Map-Set $s.winners $item.key $winner}
  }
}
function Get-PathShapeConflicts($s,$candidateIndex=$null){
  if($null -eq $candidateIndex){$candidateIndex=Get-CandidateIndex $s}
  $keys=Get-ActiveKeys $s $candidateIndex;$lookup=@{}
  foreach($key in $keys.Keys){$lookup[$key.ToLowerInvariant()]=$key}
  $issues=[Collections.Generic.List[string]]::new()
  foreach($key in @($keys.Keys)){
    $parts=$key -split '\\'
    if($parts.Count -gt 2){for($i=1;$i -lt $parts.Count-1;$i++){$prefix=($parts[0..$i] -join '\');if($lookup.ContainsKey($prefix.ToLowerInvariant())){$issues.Add("File/directory collision: '$($lookup[$prefix.ToLowerInvariant()])' is a file but is also a parent of '$key'.")}}}
    $dest=Destination $key;$parent=Split-Path -Parent $dest
    while($parent -and $parent.Length -ge $GameRoot.Length){if(Test-Path -LiteralPath $parent -PathType Leaf){$issues.Add("Live file blocks required directory: $parent (needed by $key)");break};if($parent -ieq $GameRoot){break};$parent=Split-Path -Parent $parent}
  }
  return @($issues | Select-Object -Unique)
}
function Build-DeploymentPlan($old,$next,[switch]$CaptureBases){
  $candidateIndex=Get-CandidateIndex $next
  $shape=@(Get-PathShapeConflicts $next $candidateIndex);Assert (!$shape.Count) ($shape -join ' ')
  $activeKeys=Get-ActiveKeys $next $candidateIndex
  foreach($key in @($activeKeys.Keys)){if(!(Map-Has $old.expected $key)){$dest=Destination $key;Assert (!(Test-Path -LiteralPath $dest -PathType Container)) "Destination is a folder: $dest";$base=if($CaptureBases -and (Test-Path -LiteralPath $dest -PathType Leaf)){Store-Blob $dest}else{File-Hash $dest};Map-Set $next.bases $key $base}}
  $active=Get-DesiredActive $next $candidateIndex;$targets=New-Map
  foreach($p in $active.PSObject.Properties){Map-Set $targets $p.Name ([pscustomobject]@{name=$p.Value.name;hash=$p.Value.hash;active=$true})}
  $release=[Collections.Generic.List[string]]::new()
  foreach($p in @($old.expected.PSObject.Properties)){if(!(Map-Has $active $p.Name)){$base=Map-Get $old.bases $p.Name;Map-Set $targets $p.Name ([pscustomobject]@{name='BASE';hash=$base;active=$false});$release.Add($p.Name)}}
  $changes=[Collections.Generic.List[object]]::new()
  foreach($p in $targets.PSObject.Properties){$current=if(Map-Has $old.expected $p.Name){Map-Get $old.expected $p.Name}else{File-Hash (Destination $p.Name)};if($current -ne $p.Value.hash){$changes.Add([pscustomobject]@{key=$p.Name;from=$current;to=$p.Value.hash;winner=$p.Value.name;active=$p.Value.active})}}
  return [pscustomobject]@{active=$active;targets=$targets;changes=@($changes);release=@($release)}
}
function Show-Plan($plan,[string]$description){
  Write-Host "Plan: $description"
  foreach($c in @($plan.changes | Select-Object -First 35)){$from=if($c.from){$c.from.Substring(0,[Math]::Min(10,$c.from.Length))}else{'<none>'};$to=if($c.to){$c.to.Substring(0,[Math]::Min(10,$c.to.Length))}else{'<remove>'};Write-Host "  $($c.key): $from -> $to [$($c.winner)]"}
  if($plan.changes.Count -gt 35){Write-Host "  ... and $($plan.changes.Count-35) more file change(s)"}
  Write-Host "File changes: $($plan.changes.Count); released unmanaged paths: $($plan.release.Count)"
}
function Enable-Mod($s,[string]$name){Assert (!(Map-Has $s.mods $name)) "$name is already enabled. Use Update to replace its source version.";$next=Clone-State $s;Map-Set $next.mods $name (Snapshot-Mod $name);$next.order+=@($name);Commit-Plan $s $next "Enable $name" | Out-Null}
function Disable-Mod($s,[string]$name){Assert (Map-Has $s.mods $name) "$name is OFF.";$next=Clone-State $s;Map-Remove $next.mods $name;$next.order=@($next.order | Where-Object {$_ -ne $name});foreach($p in @($next.winners.PSObject.Properties)){if($p.Value -eq $name){Map-Remove $next.winners $p.Name}};Commit-Plan $s $next "Disable $name" | Out-Null}
function Update-Mod($s,[string]$name){Assert (Map-Has $s.mods $name) "Enable $name first.";$next=Clone-State $s;Map-Set $next.mods $name (Snapshot-Mod $name);Commit-Plan $s $next "Update captured version for $name" | Out-Null}
function Set-Winner($s,[string]$key,[string]$name){$key=Normalize-Key $key;$c=@(Get-Candidates $s $key);Assert ($c.name -contains $name) "That mod does not supply $key.";$next=Clone-State $s;Map-Set $next.winners $key $name;Commit-Plan $s $next "Give $name priority for $key" | Out-Null}
function Prioritize-Mod($s,[string]$name){Assert (Map-Has $s.mods $name) "Mod is OFF: $name";$next=Clone-State $s;foreach($p in (Map-Get $s.mods $name).files.PSObject.Properties){if(@(Get-Candidates $s $p.Name).Count -gt 1){Map-Set $next.winners $p.Name $name}};Commit-Plan $s $next "Make $name win every overlapping path it supplies" | Out-Null}
function Set-SharedPolicy($s,[string]$policy){$p=$policy.Trim().ToLowerInvariant();Assert ($p -in @('identical','explicit')) 'Shared policy must be IDENTICAL or EXPLICIT.';$next=Clone-State $s;$next.sharedPolicy=$p;Commit-Plan $s $next "Set mod_hepsy identical-file ownership policy to $p" | Out-Null}
function Set-SmartConflictMode($s,[string]$mode){$m=$mode.Trim().ToLowerInvariant();Assert ($m -in @('on','off')) 'Smart conflict mode must be ON or OFF.';$next=Clone-State $s;$next.smartConflicts=$m;Commit-Plan $s $next "Set smart conflict mode to $m" | Out-Null}
function Set-ResourceProvider($s,[string]$namespace,[string]$mod){
  $ns=$namespace.Trim().TrimEnd('\').ToLowerInvariant();Assert $ns 'Enter a resource namespace.'
  $next=Clone-State $s
  if(!$mod){Map-Remove $next.resourceProviders $ns;Commit-Plan $s $next "Clear resource provider for $ns" | Out-Null;return}
  Assert (Test-Path -LiteralPath (Join-Path $ModsRoot $mod) -PathType Container) "Unknown mod folder: $mod"
  Map-Set $next.resourceProviders $ns $mod
  # An explicit namespace provider replaces stale per-file choices only where that provider actually supplies the path.
  if(Map-Has $next.mods $mod){
    $providerMod=Map-Get $next.mods $mod
    foreach($p in @($next.winners.PSObject.Properties)){
      if($p.Name.ToLowerInvariant().StartsWith($ns+'\') -and (Map-Has $providerMod.files $p.Name)){Map-Remove $next.winners $p.Name}
    }
  }
  Commit-Plan $s $next "Set shared resource provider for $ns to $mod" | Out-Null
}
function Set-ModRelationship($s,[string]$a,[string]$b,[string]$mode,[string]$winner){
  $m=$mode.Trim().ToLowerInvariant();$next=Clone-State $s
  if($m -eq 'clear'){Set-ModRelationMemory $next $a $b 'clear' $null;Commit-Plan $s $next "Clear relationship between $a and $b" | Out-Null;return}
  if($m -eq 'incompatible'){
    Assert (!(Map-Has $s.mods $a -and Map-Has $s.mods $b)) 'Disable one of these mods before marking them incompatible.'
    Set-ModRelationMemory $next $a $b 'incompatible' $null
  }elseif($m -eq 'overlay'){
    Set-ModRelationMemory $next $a $b 'overlay' $winner
    # Make a new overlay rule authoritative over stale v4 per-file choices for this exact pair.
    if(Map-Has $next.mods $a -and Map-Has $next.mods $b){
      $ma=Map-Get $next.mods $a;$mb=Map-Get $next.mods $b
      foreach($p in $ma.files.PSObject.Properties){if(Map-Has $mb.files $p.Name){Map-Set $next.winners $p.Name $winner}}
    }
  }else{throw 'Relationship must be OVERLAY, INCOMPATIBLE, or CLEAR.'}
  Commit-Plan $s $next "Set $m relationship between $a and $b" | Out-Null
}

function Stage-ModsEnabledBulk($s,[string[]]$names,[bool]$enabled){
  $next=Clone-State $s
  if($enabled){
    $append=[Collections.Generic.List[string]]::new()
    foreach($name in @($names)){
      if(!(Map-Has $next.mods $name)){Map-Set $next.mods $name (Snapshot-Mod $name);$append.Add($name)}
    }
    if($append.Count){$next.order=@($next.order)+@($append)}
    return $next
  }
  $remove=@{}
  foreach($name in @($names)){if(Map-Has $next.mods $name){$remove[$name]=$true;Map-Remove $next.mods $name}}
  if(!$remove.Count){return $next}
  $next.order=@($next.order | Where-Object {!$remove.ContainsKey([string]$_)})
  foreach($p in @($next.winners.PSObject.Properties)){if($remove.ContainsKey([string]$p.Value)){Map-Remove $next.winners $p.Name}}
  return $next
}
function Stage-ModEnabled($s,[string]$name,[bool]$enabled){return (Stage-ModsEnabledBulk $s @($name) $enabled)}
function Set-GroupWinnerMemory($s,$group,[string]$winner){
  Assert (@($group.names) -contains $winner) 'Winner is not part of this conflict group.'
  foreach($item in @($group.items)){Map-Set $s.winners $item.key $winner}
  return $s
}
function Set-OverlayMemory($s,[string]$a,[string]$b,[string]$winner){
  Assert ($winner -in @($a,$b)) 'Overlay winner must be one of the two mods.'
  Set-ModRelationMemory $s $a $b 'overlay' $winner
  if(Map-Has $s.mods $a -and Map-Has $s.mods $b){
    $ma=Map-Get $s.mods $a;$mb=Map-Get $s.mods $b
    foreach($p in $ma.files.PSObject.Properties){if(Map-Has $mb.files $p.Name){Map-Set $s.winners $p.Name $winner}}
  }
  return $s
}
function Get-ConflictGroupsForUi($s,$candidateIndex=$null){
  if($null -eq $candidateIndex){$candidateIndex=Get-CandidateIndex $s}
  $groups=@{};$pairCache=@{}
  foreach($key in @($candidateIndex.Keys)){
    $c=@($candidateIndex[$key]);if($c.Count -lt 2){continue}
    $base=Analyze-Conflict $s $key $c -IgnoreExplicit -PairCache $pairCache
    $saved=Map-Get $s.winners $key
    $names=@($c.name)
    $signature=$base.kind+'|'+(($names | ForEach-Object {$_.Length.ToString()+':'+$_}) -join ';')
    if(!$groups.ContainsKey($signature)){$groups[$signature]=[pscustomobject]@{kind=$base.kind;reason=$base.reason;names=$names;items=[Collections.Generic.List[object]]::new();blocking=$false;resolution='';winner=$null}}
    $g=$groups[$signature];$g.items.Add([pscustomobject]@{key=$key;candidates=$c})
    if($saved -and @($c.name) -contains $saved){$g.resolution='Remembered file winner';$g.winner=$saved}
    elseif($base.blocking){$g.blocking=$true;$g.resolution='Needs attention'}
    elseif($base.winner){$g.resolution='Automatic';$g.winner=$base.winner}
  }
  return @($groups.Values | Sort-Object @{Expression={if($_.blocking){0}else{1}}},@{Expression={$_.kind}},@{Expression={$_.names -join ' | '}})
}

function Show-SmartConflictSummary($s){
  Write-Host "Smart conflict mode: $($s.smartConflicts)"
  Write-Host 'Automatic: identical files share; .tex overlaps are soft; base-name + " - option" pairs are treated as overlays.'
  if($s.resourceProviders.PSObject.Properties.Count){Write-Host 'Pinned shared-resource providers:';foreach($p in $s.resourceProviders.PSObject.Properties){Write-Host "  $($p.Name) -> $($p.Value)"}}
  if($s.relations.PSObject.Properties.Count){Write-Host 'Saved mod relationships:';foreach($p in $s.relations.PSObject.Properties){$r=$p.Value;Write-Host "  $($r.a) <> $($r.b): $($r.mode)$(if($r.winner){' -> '+$r.winner}else{''})"}}
}
