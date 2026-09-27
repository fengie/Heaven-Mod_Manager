# Profile creation, switching, rename/delete, and portable export/import.
function Save-Profile($s,[string]$name){
  Assert ($name -and $name -notmatch '[\\/]') 'Enter a profile name.'
  $next=Clone-State $s
  $modsSnapshot=New-Map
  foreach($mod in @($s.order)){Map-Set $modsSnapshot $mod (Map-Get $s.mods $mod)}
  Map-Set $next.profiles $name ([pscustomobject]@{order=@($s.order);mods=$modsSnapshot;winners=(Clone-State $s.winners);sharedPolicy=$s.sharedPolicy})
  Save-State $next
  Write-Host "Saved profile: $name"
}
function Switch-Profile($s,[string]$name){
  Assert (Map-Has $s.profiles $name) "Unknown profile: $name"
  $profile=Map-Get $s.profiles $name
  $next=Clone-State $s
  $next.mods=$profile.mods
  $next.order=@($profile.order)
  $next.winners=$profile.winners
  if($profile.PSObject.Properties['sharedPolicy']){$next.sharedPolicy=$profile.sharedPolicy}
  if(Commit-Plan $s $next "Switch to profile $name"){
    $after=Load-State
    Save-Profile $after $name
  }
}
function Create-PresetProfile($s,[string]$preset,[string]$name){
  $preset=$preset.Trim().ToLowerInvariant()
  Assert ($preset -in @('all outfits','testing','minimal')) 'Preset must be all outfits, testing, or minimal.'
  if(!$name){$name=$preset}
  $modsSnapshot=New-Map;$order=@()
  if($preset -eq 'all outfits'){
    foreach($dir in @(Get-ChildItem -LiteralPath $ModsRoot -Directory | Sort-Object Name)){
      $sources=Get-SourceFiles $dir.Name
      if(@($sources.PSObject.Properties | Where-Object {$_.Name -match '(?i)^nativePC\\pl\\[fm]_equip\\'}).Count){
        $order+=@($dir.Name)
        if(Map-Has $s.mods $dir.Name){Map-Set $modsSnapshot $dir.Name (Map-Get $s.mods $dir.Name)}else{Map-Set $modsSnapshot $dir.Name (Snapshot-Mod $dir.Name)}
      }
    }
  }elseif($preset -eq 'testing'){
    $choice=Read-Host 'One mod folder name for testing'
    Assert (Test-Path -LiteralPath (Join-Path $ModsRoot $choice) -PathType Container) "Unknown mod folder: $choice"
    $order=@($choice)
    if(Map-Has $s.mods $choice){Map-Set $modsSnapshot $choice (Map-Get $s.mods $choice)}else{Map-Set $modsSnapshot $choice (Snapshot-Mod $choice)}
  }
  $next=Clone-State $s
  Map-Set $next.profiles $name ([pscustomobject]@{order=$order;mods=$modsSnapshot;winners=(New-Map);sharedPolicy=$s.sharedPolicy})
  Save-State $next
  Write-Host "Created profile '$name' from preset '$preset'. Switching later previews changes first."
}
function Rename-Profile($s,[string]$oldName,[string]$newName){
  Assert (Map-Has $s.profiles $oldName) "Unknown profile: $oldName"
  Assert ($newName -and $newName -notmatch '[\\/]') 'Enter a valid new profile name.'
  Assert (!(Map-Has $s.profiles $newName)) "Profile already exists: $newName"
  $next=Clone-State $s
  Map-Set $next.profiles $newName (Map-Get $next.profiles $oldName)
  Map-Remove $next.profiles $oldName
  Save-State $next
  Write-Host "Renamed profile '$oldName' -> '$newName'."
}
function Remove-Profile($s,[string]$name){
  Assert (Map-Has $s.profiles $name) "Unknown profile: $name"
  if((Read-Host "Type DELETE to remove profile '$name' (does not change active mods)") -cne 'DELETE'){Write-Host 'Cancelled.';return}
  $next=Clone-State $s
  Map-Remove $next.profiles $name
  Save-State $next
  Write-Host "Deleted profile: $name"
}
function Export-Profile($s,[string]$name){
  Assert (Map-Has $s.profiles $name) "Unknown profile: $name"
  $profile=Map-Get $s.profiles $name
  $exports=Join-Path $ToolRoot 'Exports';New-Item -ItemType Directory -Force -Path $exports | Out-Null
  $safe=($name -replace '[^\p{L}\p{Nd}._ -]','_').Trim()
  if(!$safe){$safe='profile'}
  $path=Join-Path $exports ($safe+'.mhwprofile.json')
  $winners=New-Map
  foreach($p in $profile.winners.PSObject.Properties){Map-Set $winners $p.Name $p.Value}
  $profilePolicy='identical'
  if($profile.PSObject.Properties['sharedPolicy']){$profilePolicy=$profile.sharedPolicy}
  $data=[ordered]@{format='MHWManualModProfile';version=1;name=$name;exportedAt=(Get-Date).ToString('o');order=@($profile.order);winners=$winners;sharedPolicy=$profilePolicy}
  Atomic-Json $path $data
  Write-Host "Exported portable profile: $path"
  return $path
}
function Import-Profile([object]$s,[string]$path,[string]$name){
  Assert (Test-Path -LiteralPath $path -PathType Leaf) "Profile file missing: $path"
  $data=Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
  Assert ($data.format -eq 'MHWManualModProfile' -and $data.version -eq 1) 'Unsupported profile export.'
  if(!$name){$name=$data.name}
  Assert ($name -and $name -notmatch '[\\/]') 'Enter a valid profile name.'
  $modsSnapshot=New-Map
  foreach($mod in @($data.order)){
    Assert (Test-Path -LiteralPath (Join-Path $ModsRoot $mod) -PathType Container) "Cannot import profile: local mod folder is missing: $mod"
    if(Map-Has $s.mods $mod){Map-Set $modsSnapshot $mod (Map-Get $s.mods $mod)}else{Map-Set $modsSnapshot $mod (Snapshot-Mod $mod)}
  }
  $winners=New-Map
  foreach($p in $data.winners.PSObject.Properties){if(@($data.order) -contains $p.Value){Map-Set $winners $p.Name $p.Value}}
  $next=Clone-State $s
  $importPolicy='identical'
  if($data.sharedPolicy -in @('identical','explicit')){$importPolicy=$data.sharedPolicy}
  Map-Set $next.profiles $name ([pscustomobject]@{order=@($data.order);mods=$modsSnapshot;winners=$winners;sharedPolicy=$importPolicy})
  Save-State $next
  Write-Host "Imported profile '$name'. It is saved but not switched on."
}

# v7 GUI-safe profile helpers (no Read-Host prompts).
function Save-ProfileAutomatic($s,[string]$name){
  Assert ($name -and $name -notmatch '[\\/]') 'Enter a profile name.'
  $next=Clone-State $s;$modsSnapshot=New-Map
  foreach($mod in @($s.order)){Map-Set $modsSnapshot $mod (Map-Get $s.mods $mod)}
  Map-Set $next.profiles $name ([pscustomobject]@{order=@($s.order);mods=$modsSnapshot;winners=(Clone-State $s.winners);sharedPolicy=$s.sharedPolicy})
  Save-State $next;Write-ManagerLog 'info' "Saved profile: $name";return $name
}
function Switch-ProfileAutomatic($s,[string]$name){
  Assert (Map-Has $s.profiles $name) "Unknown profile: $name"
  $profile=Map-Get $s.profiles $name;$next=Clone-State $s;$next.mods=$profile.mods;$next.order=@($profile.order);$next.winners=$profile.winners
  if($profile.PSObject.Properties['sharedPolicy']){$next.sharedPolicy=$profile.sharedPolicy}
  return (Commit-PlanAutomatic $s $next "Switch to profile $name")
}
function Remove-ProfileAutomatic($s,[string]$name){
  Assert (Map-Has $s.profiles $name) "Unknown profile: $name"
  $next=Clone-State $s;Map-Remove $next.profiles $name;Save-State $next;Write-ManagerLog 'info' "Deleted profile: $name"
}
