param([ValidateSet('Menu','Health','Report','Coverage','DryRun','History')][string]$Action='Menu')
try{[Console]::OutputEncoding=New-Object System.Text.UTF8Encoding($false)}catch{}
. (Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) 'Engine.ps1')
function Short-Name([string]$name){
  $n=$name -replace '-\d{2,6}-\d+(?:-\d+){0,4}$',''
  if($n.Length -gt 65){return $n.Substring(0,62)+'...'}
  return $n
}
function Get-DuplicateLabel([string]$full,[string]$short){
  if($full -match '-(\d{2,7})-([0-9][0-9-]*)(?:\s*\(\d+\))?$'){return "$short [Nexus $($Matches[1]) / $($Matches[2].TrimEnd('-'))]"}
  if($full.Length -gt 90){return $full.Substring(0,42)+' ... '+$full.Substring($full.Length-42)}
  return $full
}
function Show-List($s,[string]$filter,[string]$statusFilter){
  $dirs=@(Get-ChildItem -LiteralPath $ModsRoot -Directory | Sort-Object Name | Where-Object {
    $nameOK=($_.Name -like "*$filter*")
    $on=Map-Has $s.mods $_.Name
    $statusOK=($statusFilter -eq 'all' -or ($statusFilter -eq 'on' -and $on) -or ($statusFilter -eq 'off' -and !$on))
    $nameOK -and $statusOK
  })
  $shortCounts=@{}
  foreach($d in $dirs){$sn=Short-Name $d.Name;if(!$shortCounts.ContainsKey($sn)){$shortCounts[$sn]=0};$shortCounts[$sn]++}
  Write-Host "MHW Manual Mod Manager v7 | $($s.order.Count) enabled | smart:$($s.smartConflicts) | name: $(if($filter){$filter}else{'all'}) | status: $statusFilter"
  for($i=0;$i -lt $dirs.Count;$i++){
    $on=if(Map-Has $s.mods $dirs[$i].Name){'ON '}else{'OFF'};$sn=Short-Name $dirs[$i].Name
    $label=if($shortCounts[$sn] -gt 1){Get-DuplicateLabel $dirs[$i].Name $sn}else{$sn}
    Write-Host ('{0,3}. {1} {2}' -f ($i+1),$on,$label)
  }
  return ,$dirs
}
function Select-Mods($dirs,[string]$inputText){
  $inputText=$inputText.Trim()
  $numbers=@()
  if($inputText -ieq 'all'){if($dirs.Count){$numbers=1..$dirs.Count}}
  else{
    foreach($part in ($inputText -split ',')){
      $part=$part.Trim()
      if($part -match '^(\d+)-(\d+)$'){
        $a=[int]$Matches[1];$b=[int]$Matches[2]
        Assert ($a -le $b -and $a -gt 0 -and $b -le $dirs.Count) "Invalid range: $part"
        $numbers+=@($a..$b)
      }elseif($part -match '^\d+$' -and [int]$part -ge 1 -and [int]$part -le $dirs.Count){$numbers+=@([int]$part)}
      else{throw "Invalid selection '$part'. Use listed numbers, comma lists, a range like 2-7, or ALL."}
    }
  }
  return ,@($numbers | Select-Object -Unique | ForEach-Object {$dirs[$_-1].Name})
}
function Parse-CommandLine([string]$line){
  $trim=$line.Trim()
  if(!$trim){return [pscustomobject]@{cmd='';arg=''}}
  if($trim -match '^(?i)mod\s+priority(?:\s+(.*))?$'){return [pscustomobject]@{cmd='priority';arg=$Matches[1].Trim()}}
  if($trim -match '^(?i)file\s+priority(?:\s+(.*))?$'){return [pscustomobject]@{cmd='file';arg=$Matches[1].Trim()}}
  if($trim -match '^(\S+)\s*(.*)$'){return [pscustomobject]@{cmd=$Matches[1].ToLowerInvariant();arg=$Matches[2].Trim()}}
  return [pscustomobject]@{cmd=$trim.ToLowerInvariant();arg=''}
}
function Profile-Menu($s){
  Write-Host "Profiles: $(@($s.profiles.PSObject.Properties.Name | Sort-Object) -join ', ')"
  Write-Host 'save | switch | preset | rename | delete | export | import'
  $mode=(Read-Host 'Profile command').Trim().ToLowerInvariant()
  if($mode -eq 'save'){Save-Profile $s (Read-Host 'Profile name')}
  elseif($mode -eq 'switch'){Switch-Profile $s (Read-Host 'Profile name')}
  elseif($mode -eq 'preset'){
    $preset=Read-Host 'Preset: all outfits / testing / minimal'
    $name=Read-Host 'Saved profile name (blank uses preset name)'
    Create-PresetProfile $s $preset $name
  }elseif($mode -eq 'rename'){Rename-Profile $s (Read-Host 'Old profile name') (Read-Host 'New profile name')}
  elseif($mode -eq 'delete'){Remove-Profile $s (Read-Host 'Profile name')}
  elseif($mode -eq 'export'){Export-Profile $s (Read-Host 'Profile name') | Out-Null}
  elseif($mode -eq 'import'){Import-Profile $s (Read-Host 'Full .mhwprofile.json path') (Read-Host 'Saved profile name (blank uses exported name)')}
  else{Write-Host 'Unknown profile command.'}
}
function Main {
  $s=Initialize-State
  if($Action -eq 'Health'){Health-Report $s | Out-Null;return}
  if($Action -eq 'Report'){Conflict-Report $s | Out-Null;return}
  if($Action -eq 'Coverage'){Coverage-Page $s | Out-Null;return}
  if($Action -eq 'History'){Show-History;return}
  if($Action -eq 'DryRun'){Apply-State $s (Clone-State $s) -DryRun -Description 'Current-state verification' | Out-Null;return}
  $filter='';$statusFilter='all'
  while($true){
    $s=Load-State
    Clear-Host
    $dirs=Show-List $s $filter $statusFilter
    Write-Host "`n1 enable | 2 disable | 3 update | 4 mod priority | 5 file priority | 6 profiles"
    Write-Host '7 conflicts | 8 armor | 9 health | 10 import | 11 history/undo | 12 settings | 13 smart conflicts | / filter | 0 exit'
    Write-Host 'Tip: commands can include the selection, e.g.  enable 2,5   or   disable 8-12'
    $parsed=Parse-CommandLine (Read-Host 'Command')
    $cmd=$parsed.cmd;$inline=$parsed.arg
    if($cmd -in @('0','exit','quit')){return}
    if($cmd -in @('/','search','filter')){
      if($inline){$filter=$inline}else{$filter=Read-Host 'Name contains (blank clears)'}
      $sf=(Read-Host 'Status filter: all / on / off (blank keeps current)').Trim().ToLowerInvariant()
      if($sf){Assert ($sf -in @('all','on','off')) 'Status filter must be all, on, or off.';$statusFilter=$sf}
      continue
    }
    try{
      if($cmd -in @('1','enable','on','2','disable','off','3','update','replace','4','priority','mod')){
        $selection=if($inline){$inline}else{Read-Host 'Mod number(s), comma list, range, or ALL'}
        $chosen=Select-Mods $dirs $selection
        Assert $chosen.Count 'No mods selected.'
        if($cmd -in @('4','priority','mod')){Assert ($chosen.Count -eq 1) 'Choose one priority mod.';Prioritize-Mod $s $chosen[0]}
        elseif($cmd -in @('3','update','replace')){Assert ($chosen.Count -eq 1) 'Update one mod at a time.';Update-Mod $s $chosen[0]}
        else{
          $next=Clone-State $s
          foreach($name in $chosen){
            if($cmd -in @('1','enable','on')){
              if(Map-Has $next.mods $name){Write-Host "Already ON: $name";continue}
              Map-Set $next.mods $name (Snapshot-Mod $name);$next.order+=@($name)
            }else{
              if(!(Map-Has $next.mods $name)){Write-Host "Already OFF: $name";continue}
              Map-Remove $next.mods $name;$next.order=@($next.order | Where-Object {$_ -ne $name})
              foreach($p in @($next.winners.PSObject.Properties)){if($p.Value -eq $name){Map-Remove $next.winners $p.Name}}
            }
          }
          Commit-Plan $s $next "$cmd $($chosen.Count) mod(s)" | Out-Null
        }
      }elseif($cmd -in @('5','file','filepriority')){
        Set-Winner $s (Read-Host 'Game path (nativePC\... or root\...)') (Read-Host 'Winner mod folder name')
      }elseif($cmd -in @('6','profiles','profile')){Profile-Menu $s}
      elseif($cmd -in @('7','conflicts','report')){Conflict-Report $s | Out-Null}
      elseif($cmd -in @('8','armor','coverage')){Coverage-Page $s | Out-Null}
      elseif($cmd -in @('9','health','check')){Health-Report $s | Out-Null}
      elseif($cmd -in @('10','import')){Import-Archive (Read-Host 'Full archive path') (Read-Host 'Short new mod name')}
      elseif($cmd -in @('11','history','undo')){
        Show-History
        if((Read-Host 'Type UNDO to undo the latest deployment, otherwise Enter').Trim() -ceq 'UNDO'){Undo-LastDeployment $s}
      }elseif($cmd -in @('12','settings','setting')){
        Write-Host "mod_hepsy identical-file ownership policy: $($s.sharedPolicy)"
        Write-Host 'IDENTICAL = byte-identical shared files need no owner prompt.'
        Write-Host 'EXPLICIT = byte-identical mod_hepsy files also record an explicit owner.'
        $p=(Read-Host 'New policy: identical / explicit (blank keeps current)').Trim()
        if($p){Set-SharedPolicy $s $p}
      }elseif($cmd -in @('13','smart','relationships','relationship')){
        Show-SmartConflictSummary $s
        Write-Host 'Commands: mode | overlay | incompatible | clear | provider | clearprovider | Enter'
        $sc=(Read-Host 'Smart conflict command').Trim().ToLowerInvariant()
        if($sc -eq 'mode'){Set-SmartConflictMode $s (Read-Host 'Smart mode: on / off')}
        elseif($sc -in @('overlay','incompatible','clear')){
          $pick=Read-Host 'Choose exactly two mod numbers from the current list, e.g. 22,24'
          $pair=Select-Mods $dirs $pick;Assert ($pair.Count -eq 2) 'Choose exactly two mods.'
          if($sc -eq 'overlay'){
            Write-Host "[1] $($pair[0])";Write-Host "[2] $($pair[1])"
            $w=(Read-Host 'Which mod should win overlapping files? 1 or 2').Trim();Assert ($w -in @('1','2')) 'Enter 1 or 2.'
            Set-ModRelationship $s $pair[0] $pair[1] 'overlay' $pair[[int]$w-1]
          }elseif($sc -eq 'incompatible'){Set-ModRelationship $s $pair[0] $pair[1] 'incompatible' $null}
          else{Set-ModRelationship $s $pair[0] $pair[1] 'clear' $null}
        }elseif($sc -eq 'provider'){
          $one=Select-Mods $dirs (Read-Host 'Provider mod number');Assert ($one.Count -eq 1) 'Choose one mod.'
          $ns=(Read-Host 'Resource namespace (blank = nativePC\pl\f_equip\mod_hepsy)').Trim();if(!$ns){$ns='nativePC\pl\f_equip\mod_hepsy'}
          Set-ResourceProvider $s $ns $one[0]
        }elseif($sc -eq 'clearprovider'){
          $ns=(Read-Host 'Resource namespace (blank = nativePC\pl\f_equip\mod_hepsy)').Trim();if(!$ns){$ns='nativePC\pl\f_equip\mod_hepsy'}
          Set-ResourceProvider $s $ns ''
        }
      }else{Write-Host "Unknown command '$cmd'. Enter a menu number or command word."}
    }catch{Write-Host "ERROR: $($_.Exception.Message)" -ForegroundColor Red}
    Read-Host 'Press Enter to continue' | Out-Null
  }
}
try{Main}catch{Write-Host "ERROR: $($_.Exception.Message)" -ForegroundColor Red;exit 1}
