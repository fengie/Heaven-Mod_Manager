# MHW Manual Mod Manager v7 core. Windows PowerShell 5.1 compatible.
$ErrorActionPreference='Stop'
$ToolRoot=Split-Path -Parent $MyInvocation.MyCommand.Path
$ModsRoot=Join-Path $ToolRoot 'Mods'
$StateRoot=Join-Path $ToolRoot 'State'
$V2Root=Join-Path $StateRoot 'V2'
$BlobRoot=Join-Path $V2Root 'Blobs'
$HistoryRoot=Join-Path $V2Root 'History'
$StateFile=Join-Path $V2Root 'state.json'
$LogRoot=Join-Path $V2Root 'Logs'
$UiSettingsFile=Join-Path $V2Root 'ui-settings.json'

function Get-SteamLibraryRoots {
  $roots=[Collections.Generic.List[string]]::new()
  $steam=$null
  try{$steam=(Get-ItemProperty -Path 'HKCU:\Software\Valve\Steam' -ErrorAction Stop).SteamPath}catch{}
  if($steam){$roots.Add(($steam -replace '/','\'))}
  $default='C:\Program Files (x86)\Steam'
  if((Test-Path -LiteralPath $default -PathType Container) -and !($roots -contains $default)){$roots.Add($default)}
  foreach($root in @($roots.ToArray())){
    $vdf=Join-Path $root 'steamapps\libraryfolders.vdf'
    if(!(Test-Path -LiteralPath $vdf -PathType Leaf)){continue}
    try{
      foreach($line in Get-Content -LiteralPath $vdf -ErrorAction Stop){
        if($line -match '^\s*"path"\s+"([^"]+)"'){
          $candidate=($Matches[1] -replace '\\','\')
          if((Test-Path -LiteralPath $candidate -PathType Container) -and !($roots -contains $candidate)){$roots.Add($candidate)}
        }
      }
    }catch{}
  }
  return @($roots | Select-Object -Unique)
}
function Resolve-GameRoot {
  if($env:MHW_MANAGER_TEST_GAME_ROOT){return $env:MHW_MANAGER_TEST_GAME_ROOT}
  if(Test-Path -LiteralPath $StateFile -PathType Leaf){
    try{
      $raw=Get-Content -LiteralPath $StateFile -Raw | ConvertFrom-Json
      if($raw.gameRoot -and (Test-Path -LiteralPath $raw.gameRoot -PathType Container)){return $raw.gameRoot}
    }catch{}
  }
  if(Test-Path -LiteralPath $UiSettingsFile -PathType Leaf){
    try{
      $cfg=Get-Content -LiteralPath $UiSettingsFile -Raw | ConvertFrom-Json
      if($cfg.gameRoot -and (Test-Path -LiteralPath $cfg.gameRoot -PathType Container)){return $cfg.gameRoot}
    }catch{}
  }
  foreach($library in @(Get-SteamLibraryRoots)){
    $candidate=Join-Path $library 'steamapps\common\Monster Hunter World'
    if(Test-Path -LiteralPath (Join-Path $candidate 'MonsterHunterWorld.exe') -PathType Leaf){return $candidate}
  }
  return 'C:\Program Files (x86)\Steam\steamapps\common\Monster Hunter World'
}
$GameRoot=Resolve-GameRoot
$NativeRoot=Join-Path $GameRoot 'nativePC'

function Write-ManagerLog([string]$level,[string]$message,[object]$data=$null){
  try{
    New-Item -ItemType Directory -Force -Path $LogRoot | Out-Null
    $entry=[ordered]@{time=(Get-Date).ToUniversalTime().ToString('o');level=$level;message=$message}
    if($null -ne $data){$entry.data=$data}
    ($entry | ConvertTo-Json -Depth 20 -Compress) | Add-Content -LiteralPath (Join-Path $LogRoot ((Get-Date).ToString('yyyy-MM-dd')+'.jsonl')) -Encoding UTF8
  }catch{}
}
function Save-UiSettings([hashtable]$values){
  $current=[ordered]@{}
  if(Test-Path -LiteralPath $UiSettingsFile -PathType Leaf){try{$obj=Get-Content -LiteralPath $UiSettingsFile -Raw | ConvertFrom-Json;foreach($p in $obj.PSObject.Properties){$current[$p.Name]=$p.Value}}catch{}}
  foreach($k in $values.Keys){$current[$k]=$values[$k]}
  Atomic-Json $UiSettingsFile $current
}

function Assert($condition,$message) { if (!$condition) { throw $message } }
function Normalize-Key([string]$key) {
  $k=$key.Replace('/','\').TrimStart('\')
  Assert ($k -match '^(nativePC|root)\\' -and $k -notmatch '(^|\\)\.\.(\\|$)' -and $k -notmatch ':') "Invalid path: $key"
  return $k
}
function Destination([string]$key) {
  $key=Normalize-Key $key
  if ($key.StartsWith('root\',[StringComparison]::OrdinalIgnoreCase)) { return Join-Path $GameRoot $key.Substring(5) }
  return Join-Path $NativeRoot $key.Substring(9)
}
function File-Hash([string]$path) {
  if (Test-Path -LiteralPath $path -PathType Leaf) { return (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() }
  return $null
}
function Blob-Path([string]$hash) { Join-Path $BlobRoot $hash }
function Store-Blob([string]$path) {
  $hash=File-Hash $path
  Assert $hash "File missing: $path"
  $dest=Blob-Path $hash
  if (!(Test-Path -LiteralPath $dest)) { Copy-Item -LiteralPath $path -Destination $dest }
  return $hash
}
function Atomic-Json([string]$path,$value) {
  $tmp=$path+'.new'
  $value | ConvertTo-Json -Depth 60 | Set-Content -LiteralPath $tmp -Encoding UTF8
  Move-Item -LiteralPath $tmp -Destination $path -Force
}
function New-Map { return [pscustomobject]@{} }
function Map-Get($map,[string]$key) {
  if($null -eq $map){return $null}
  $prop=$map.PSObject.Properties[$key]
  if($null -eq $prop){return $null}
  return $prop.Value
}
function Map-Has($map,[string]$key) { return ($null -ne $map -and $null -ne $map.PSObject.Properties[$key]) }
function Map-Set($map,[string]$key,$value) { $map | Add-Member -NotePropertyName $key -NotePropertyValue $value -Force }
function Map-Remove($map,[string]$key) { if($null -ne $map){$map.PSObject.Properties.Remove($key)} }
function Copy-MapShallow($map){
  $copy=New-Map
  if($null -ne $map){foreach($p in $map.PSObject.Properties){Map-Set $copy $p.Name $p.Value}}
  return $copy
}
function Clone-State($s){
  if($null -eq $s){return $null}
  # Manager state is immutable below its top-level maps during planning. A shallow map copy avoids
  # repeatedly serializing thousands of captured file hashes just to toggle one mod or winner.
  if($s.PSObject.Properties['schema'] -and $s.PSObject.Properties['mods']){
    $copy=[pscustomobject]@{}
    $mapNames=@('mods','winners','bases','expected','profiles','relations','resourceProviders')
    foreach($p in $s.PSObject.Properties){
      if($p.Name -in $mapNames){$value=Copy-MapShallow $p.Value}
      elseif($p.Name -eq 'order'){$value=@($p.Value)}
      else{$value=$p.Value}
      $copy | Add-Member -NotePropertyName $p.Name -NotePropertyValue $value -Force
    }
    return (Ensure-StateShape $copy)
  }
  return ($s | ConvertTo-Json -Depth 60 | ConvertFrom-Json)
}
function New-State {
  return [pscustomobject]@{schema=2;gameRoot=$GameRoot;mods=(New-Map);order=@();winners=(New-Map);bases=(New-Map);expected=(New-Map);profiles=(New-Map);sharedPolicy='identical';smartConflicts='on';relations=(New-Map);resourceProviders=(New-Map)}
}
function Ensure-StateShape($s) {
  foreach($name in @('mods','winners','bases','expected','profiles','relations','resourceProviders')){
    if(!(Map-Has $s $name)){$s | Add-Member -NotePropertyName $name -NotePropertyValue (New-Map) -Force}
  }
  if(!(Map-Has $s 'order')){$s | Add-Member -NotePropertyName order -NotePropertyValue @() -Force}
  if(!(Map-Has $s 'sharedPolicy')){$s | Add-Member -NotePropertyName sharedPolicy -NotePropertyValue 'identical' -Force}
  if($s.sharedPolicy -notin @('identical','explicit')){$s.sharedPolicy='identical'}
  if(!(Map-Has $s 'smartConflicts')){$s | Add-Member -NotePropertyName smartConflicts -NotePropertyValue 'on' -Force}
  if($s.smartConflicts -notin @('on','off')){$s.smartConflicts='on'}
  return $s
}
function Load-State {
  if (!(Test-Path -LiteralPath $StateFile)) { return $null }
  $s=Get-Content -LiteralPath $StateFile -Raw | ConvertFrom-Json
  Assert ($s.schema -eq 2) 'Unsupported state version.'
  Assert ($s.gameRoot -ieq $GameRoot) 'Game root has changed. Edit $GameRoot after backing up State; migration is required.'
  return (Ensure-StateShape $s)
}
function Save-State($s) { Atomic-Json $StateFile $s }

function Get-SourceFiles([string]$name) {
  Assert ($name -and $name -notmatch '[\\/]') 'Choose one named Mods folder.'
  $folder=Join-Path $ModsRoot $name
  Assert (Test-Path -LiteralPath $folder -PathType Container) "Mod folder missing: $folder"
  $native=Join-Path $folder 'nativePC'
  $rootFiles=Join-Path $folder 'GameRoot'
  $source=if(Test-Path -LiteralPath $native -PathType Container){$native}else{$folder}
  $result=New-Map
  foreach($f in @(Get-ChildItem -LiteralPath $source -Recurse -File)) {
    if($f.FullName -match '[\\/](?:__MACOSX|\.git|GameRoot)[\\/]' -or $f.Name -in @('Thumbs.db','desktop.ini','readme.txt','README.md')){continue}
    $relative=$f.FullName.Substring($source.TrimEnd('\').Length+1)
    if ($source -eq $folder -and $relative -match '^(?:nativePC|GameRoot)\\') { continue }
    $key=Normalize-Key ('nativePC\'+$relative)
    Map-Set $result $key $f.FullName
  }
  if(Test-Path -LiteralPath $rootFiles -PathType Container){
    foreach($f in @(Get-ChildItem -LiteralPath $rootFiles -Recurse -File)) {
      $key=Normalize-Key ('root\'+$f.FullName.Substring($rootFiles.TrimEnd('\').Length+1))
      Map-Set $result $key $f.FullName
    }
  }
  Assert ($result.PSObject.Properties.Count -gt 0) "No deployable files found in $folder. Check archive layout."
  return $result
}
function Snapshot-Mod([string]$name) {
  $sources=Get-SourceFiles $name
  $files=New-Map
  foreach($p in $sources.PSObject.Properties){ Map-Set $files $p.Name (Store-Blob $p.Value) }
  return [pscustomobject]@{files=$files;importedAt=(Get-Date).ToString('o')}
}
function Get-Candidates($s,[string]$key,$candidateIndex=$null) {
  if($null -ne $candidateIndex -and $candidateIndex.ContainsKey($key)){return @($candidateIndex[$key])}
  $c=[Collections.Generic.List[object]]::new()
  foreach($name in @($s.order)) {
    $m=Map-Get $s.mods $name
    if($m -and (Map-Has $m.files $key)) { $c.Add([pscustomobject]@{name=$name;hash=(Map-Get $m.files $key)}) }
  }
  return @($c)
}
function Verify-Live($s) {
  foreach($p in $s.expected.PSObject.Properties){
    $actual=File-Hash (Destination $p.Name)
    Assert ($actual -eq $p.Value) "Changed file: $($p.Name). Expected $($p.Value), found $actual. Run Health; deployment stopped."
  }
}
function Assert-GameClosed { Assert (!(Get-Process -Name 'MonsterHunterWorld' -ErrorAction SilentlyContinue)) 'Close Monster Hunter: World before changing files.' }

function Release-Stale-V2Ownership($s) {
  $active=@{}
  foreach($name in @($s.order)){
    $m=Map-Get $s.mods $name
    if(!$m){continue}
    foreach($p in $m.files.PSObject.Properties){$active[$p.Name.ToLowerInvariant()]=$true}
  }
  $changed=$false
  foreach($p in @($s.expected.PSObject.Properties)){
    if(!$active.ContainsKey($p.Name.ToLowerInvariant())){
      Map-Remove $s.expected $p.Name
      Map-Remove $s.bases $p.Name
      Map-Remove $s.winners $p.Name
      $changed=$true
    }
  }
  if($changed){
    $snapshot=Join-Path $V2Root 'state-before-v4-upgrade.json'
    if(!(Test-Path -LiteralPath $snapshot -PathType Leaf) -and (Test-Path -LiteralPath $StateFile -PathType Leaf)){Copy-Item -LiteralPath $StateFile -Destination $snapshot -Force}
    Save-State $s
    Write-Host 'Upgraded state: released restored unmanaged paths left tracked by v2. Pre-upgrade state was preserved.'
  }
  return $s
}

function Initialize-State {
  New-Item -ItemType Directory -Force -Path $ModsRoot,$StateRoot,$V2Root,$BlobRoot,$HistoryRoot,$LogRoot | Out-Null
  Recover-Transaction
  $s=Load-State
  if($s){return (Release-Stale-V2Ownership $s)}
  $legacy=@(Get-ChildItem -LiteralPath $StateRoot -Filter '*.json' -File | Where-Object {$_.Name -ne 'state.json'})
  $s=New-State
  if(!$legacy.Count){Save-State $s;return Load-State}
  $archive=Join-Path $V2Root 'LegacySnapshot'
  New-Item -ItemType Directory -Force -Path $archive | Out-Null
  foreach($f in $legacy){Copy-Item -LiteralPath $f.FullName -Destination (Join-Path $archive $f.Name) -Force}
  $oldBackups=Join-Path $StateRoot 'Backups'
  if(Test-Path -LiteralPath $oldBackups){Copy-Item -LiteralPath $oldBackups -Destination (Join-Path $archive 'Backups') -Recurse -Force}
  $records=@{}
  foreach($f in @($legacy | Sort-Object LastWriteTime)){
    $m=Get-Content -LiteralPath $f.FullName -Raw | ConvertFrom-Json
    $name=$m.name
    $files=New-Map
    foreach($r in @($m.files)){
      $key=Normalize-Key ('nativePC\'+$r.relative)
      Map-Set $files $key $r.hash.ToLowerInvariant()
      if($r.owned -eq $false){continue}
      if(!$records.ContainsKey($key)){$records[$key]=@()}
      $records[$key]+=[pscustomobject]@{name=$name;hash=$r.hash.ToLowerInvariant();backupRoot=$m.backupRoot;backedUp=$r.backedUp;relative=$r.relative;previousOwner=$r.previousOwner}
    }
    Map-Set $s.mods $name ([pscustomobject]@{files=$files;importedAt='legacy'})
    $s.order+=@($name)
  }
  foreach($key in @($records.Keys)){
    $layers=@($records[$key])
    $live=File-Hash (Destination $key)
    Assert $live "Migration stopped: $key is missing. Legacy files remain untouched."
    $matching=@($layers | Where-Object {$_.hash -eq $live})
    Assert ($matching.Count -gt 0) "Migration stopped: $key differs from all legacy manifests. Legacy files remain untouched."
    $top=$matching[-1]
    Map-Set $s.winners $key $top.name
    Map-Set $s.expected $key $live
    $roots=@($layers | Where-Object { !$_.previousOwner })
    Assert ($roots.Count -eq 1) "Migration stopped: ambiguous original owner for $key. Legacy files remain untouched."
    $oldest=$roots[0]
    $baseHash=$null
    if($oldest.backedUp -eq $true){
      $basePath=Join-Path (Join-Path $oldBackups $oldest.backupRoot) $oldest.relative
      Assert (Test-Path -LiteralPath $basePath -PathType Leaf) "Migration stopped: missing legacy backup $basePath."
      $baseHash=Store-Blob $basePath
    }
    Map-Set $s.bases $key $baseHash
    foreach($layer in $layers){
      $hash=$layer.hash
      if(Test-Path -LiteralPath (Blob-Path $hash)){continue}
      if($hash -eq $live){Store-Blob (Destination $key) | Out-Null;continue}
      $folder=Join-Path $ModsRoot $layer.name
      if(Test-Path -LiteralPath $folder -PathType Container){
        try{
          $source=Get-SourceFiles $layer.name
          $p=Map-Get $source $key
          if($p -and (File-Hash $p) -eq $hash){Store-Blob $p | Out-Null;continue}
        }catch{}
      }
      $found=$false
      foreach($other in $layers){
        if($other.backedUp -eq $true){
          $backup=Join-Path (Join-Path $oldBackups $other.backupRoot) $other.relative
          if((File-Hash $backup) -eq $hash){Store-Blob $backup | Out-Null;$found=$true;break}
        }
      }
      Assert $found "Migration stopped: cannot recover earlier version $hash of $key. Legacy files remain untouched."
    }
  }
  Save-State $s
  Write-Host "Migrated $($legacy.Count) enabled mods. Old manifests and backups remain in State; snapshot in State\V2\LegacySnapshot."
  return Load-State
}

$ModuleRoot=Join-Path $ToolRoot 'Modules'
foreach($module in @('Planner.ps1','Deployment.ps1','Profiles.ps1','Import.ps1','Reports.ps1','Diagnostics.ps1')){
  $path=Join-Path $ModuleRoot $module
  Assert (Test-Path -LiteralPath $path -PathType Leaf) "Missing manager module: $module"
  . $path
}
