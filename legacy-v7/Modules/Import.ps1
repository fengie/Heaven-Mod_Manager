# Safe archive import. ZIP is built in; 7z/RAR use 7z.exe/7za.exe when available.
function Select-ArchiveRootFiles($rootFiles){
  $files=@($rootFiles)
  if(!$files.Count){return @()}
  Write-Host 'Files beside the selected layout that may belong in the game root:'
  for($i=0;$i -lt $files.Count;$i++){Write-Host ('  [{0}] {1}' -f ($i+1),$files[$i].Name)}
  $pick=(Read-Host 'Choose numbers/comma list, ALL, or blank to skip').Trim()
  if(!$pick){return @()}
  if($pick -ieq 'all'){return $files}
  $chosen=@()
  foreach($part in ($pick -split ',')){
    $n=$part.Trim()
    Assert ($n -match '^\d+$' -and [int]$n -ge 1 -and [int]$n -le $files.Count) "Invalid root-file selection: $n"
    $chosen+=@($files[[int]$n-1])
  }
  return @($chosen | Select-Object -Unique)
}
function Import-Archive([string]$archive,[string]$name){
  Assert (Test-Path -LiteralPath $archive -PathType Leaf) "Archive missing: $archive"
  Assert ($name -and $name -notmatch '[\\/:*?"<>|]') 'Choose a valid, unique folder name.'
  $final=Join-Path $ModsRoot $name
  Assert (!(Test-Path -LiteralPath $final)) "Folder already exists: $final"
  $stage=Join-Path $ToolRoot ('Import-Staging-'+[guid]::NewGuid().ToString('N'))
  New-Item -ItemType Directory -Path $stage | Out-Null
  try{
    $ext=[IO.Path]::GetExtension($archive).ToLowerInvariant()
    if($ext -eq '.zip'){
      Add-Type -AssemblyName System.IO.Compression.FileSystem
      $zip=[IO.Compression.ZipFile]::OpenRead($archive)
      try{
        foreach($e in $zip.Entries){
          $n=$e.FullName.Replace('/','\')
          Assert ($n -notmatch '(^[\\/]|^[a-zA-Z]:|(^|\\)\.\.(\\|$))') "Unsafe archive entry: $n"
        }
      }finally{$zip.Dispose()}
      Expand-Archive -LiteralPath $archive -DestinationPath $stage
    }elseif($ext -in @('.7z','.rar')){
      $tool=Get-Command '7z.exe' -ErrorAction SilentlyContinue
      if(!$tool){$tool=Get-Command '7za.exe' -ErrorAction SilentlyContinue}
      Assert $tool '7z.exe or 7za.exe is required for RAR/7z imports. Extract it manually into a named Mods folder instead.'
      & $tool.Source x $archive ('-o'+$stage) -y | Out-Null
      Assert ($LASTEXITCODE -eq 0) '7-Zip extraction failed.'
    }else{throw 'Supported archives: ZIP, 7z, RAR.'}
    $stageFull=[IO.Path]::GetFullPath($stage)+[IO.Path]::DirectorySeparatorChar
    foreach($f in @(Get-ChildItem -LiteralPath $stage -Recurse -Force)){
      Assert ([IO.Path]::GetFullPath($f.FullName).StartsWith($stageFull,[StringComparison]::OrdinalIgnoreCase)) 'Archive escaped staging folder.'
      Assert (!(($f.Attributes -band [IO.FileAttributes]::ReparsePoint))) 'Archive contains a link; import refused.'
    }
    $candidates=@(Get-ChildItem -LiteralPath $stage -Recurse -Directory | Where-Object {$_.Name -ieq 'nativePC'} | Sort-Object FullName)
    $chosen=$null
    if($candidates.Count){
      Write-Host 'Install layouts found:'
      for($i=0;$i -lt $candidates.Count;$i++){Write-Host "  $($i+1). $($candidates[$i].FullName.Substring($stage.Length+1))"}
      $pick=Read-Host 'Choose ONE nativePC layout number (blank cancels)'
      Assert ($pick -match '^\d+$' -and [int]$pick -ge 1 -and [int]$pick -le $candidates.Count) 'Import cancelled.'
      $chosen=$candidates[[int]$pick-1]
    }else{
      $candidates=@(Get-ChildItem -LiteralPath $stage -Recurse -Directory | Where-Object {@(Get-ChildItem -LiteralPath $_.FullName -Directory -ErrorAction SilentlyContinue | Where-Object {$_.Name -in @('pl','wp','common','quest','otomo','em')}).Count} | Sort-Object FullName)
      Assert $candidates.Count 'No nativePC or recognizable direct game-path layout was found in the archive.'
      Write-Host 'No nativePC folder found. Possible direct game-path layouts:'
      for($i=0;$i -lt $candidates.Count;$i++){Write-Host "  $($i+1). $($candidates[$i].FullName.Substring($stage.Length+1))"}
      $pick=Read-Host 'Choose ONE directory number (blank cancels)'
      Assert ($pick -match '^\d+$' -and [int]$pick -ge 1 -and [int]$pick -le $candidates.Count) 'Import cancelled.'
      $chosen=$candidates[[int]$pick-1]
    }
    New-Item -ItemType Directory -Path $final | Out-Null
    Copy-Item -LiteralPath $chosen.FullName -Destination (Join-Path $final 'nativePC') -Recurse
    $parent=$chosen.Parent.FullName
    $rootFiles=@(Get-ChildItem -LiteralPath $parent -File | Where-Object {$_.Extension.ToLowerInvariant() -in @('.dll','.ini','.json','.exe','.config')})
    $selected=@(Select-ArchiveRootFiles $rootFiles)
    if($selected.Count){
      $dest=Join-Path $final 'GameRoot';New-Item -ItemType Directory -Path $dest | Out-Null
      foreach($f in $selected){Copy-Item -LiteralPath $f.FullName -Destination $dest}
    }
    $readmes=@(Get-ChildItem -LiteralPath $parent -File | Where-Object {$_.Name -match '(?i)readme|install|require'})
    foreach($file in $readmes){Copy-Item -LiteralPath $file.FullName -Destination $final -ErrorAction SilentlyContinue}
    Write-Host "Imported one selected layout to $final. Nothing was enabled automatically; inspect the folder first."
  }catch{
    if(Test-Path -LiteralPath $final){Remove-Item -LiteralPath $final -Recurse -Force}
    throw
  }finally{if(Test-Path -LiteralPath $stage){Remove-Item -LiteralPath $stage -Recurse -Force}}
}

# v7 GUI-safe archive inspection/import. Nothing here prompts in a console.
function Expand-ArchiveSafe([string]$archive,[string]$stage){
  $ext=[IO.Path]::GetExtension($archive).ToLowerInvariant()
  if($ext -eq '.zip'){
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip=[IO.Compression.ZipFile]::OpenRead($archive)
    try{foreach($e in $zip.Entries){$n=$e.FullName.Replace('/','\');Assert ($n -notmatch '(^[\\/]|^[a-zA-Z]:|(^|\\)\.\.(\\|$))') "Unsafe archive entry: $n"}}finally{$zip.Dispose()}
    Expand-Archive -LiteralPath $archive -DestinationPath $stage
  }elseif($ext -in @('.7z','.rar')){
    $tool=Get-Command '7z.exe' -ErrorAction SilentlyContinue;if(!$tool){$tool=Get-Command '7za.exe' -ErrorAction SilentlyContinue}
    Assert $tool '7z.exe or 7za.exe is required for RAR/7z imports.'
    & $tool.Source x $archive ('-o'+$stage) -y | Out-Null;Assert ($LASTEXITCODE -eq 0) '7-Zip extraction failed.'
  }else{throw 'Supported archives: ZIP, 7z, RAR.'}
  $stageFull=[IO.Path]::GetFullPath($stage)+[IO.Path]::DirectorySeparatorChar
  foreach($f in @(Get-ChildItem -LiteralPath $stage -Recurse -Force)){
    Assert ([IO.Path]::GetFullPath($f.FullName).StartsWith($stageFull,[StringComparison]::OrdinalIgnoreCase)) 'Archive escaped staging folder.'
    Assert (!(($f.Attributes -band [IO.FileAttributes]::ReparsePoint))) 'Archive contains a link; import refused.'
  }
}
function New-ImportInspection([string]$archive){
  Assert (Test-Path -LiteralPath $archive -PathType Leaf) "Archive missing: $archive"
  $stage=Join-Path $ToolRoot ('Import-Staging-'+[guid]::NewGuid().ToString('N'));New-Item -ItemType Directory -Path $stage | Out-Null
  try{
    Expand-ArchiveSafe $archive $stage
    $layouts=@()
    foreach($d in @(Get-ChildItem -LiteralPath $stage -Recurse -Directory | Where-Object {$_.Name -ieq 'nativePC'} | Sort-Object FullName)){
      $layouts+=@([pscustomobject]@{fullPath=$d.FullName;relative=$d.FullName.Substring($stage.Length+1);kind='nativePC'})
    }
    if(!$layouts.Count){
      foreach($d in @(Get-ChildItem -LiteralPath $stage -Recurse -Directory | Where-Object {@(Get-ChildItem -LiteralPath $_.FullName -Directory -ErrorAction SilentlyContinue | Where-Object {$_.Name -in @('pl','wp','common','quest','otomo','em')}).Count} | Sort-Object FullName)){
        $layouts+=@([pscustomobject]@{fullPath=$d.FullName;relative=$d.FullName.Substring($stage.Length+1);kind='direct'})
      }
    }
    Assert $layouts.Count 'No nativePC or recognizable direct game-path layout was found in the archive.'
    return [pscustomobject]@{archive=$archive;stage=$stage;layouts=@($layouts)}
  }catch{if(Test-Path -LiteralPath $stage){Remove-Item -LiteralPath $stage -Recurse -Force};throw}
}
function Get-ImportRootFiles($inspection,[string]$layoutPath){
  $layout=@($inspection.layouts | Where-Object {$_.fullPath -eq $layoutPath} | Select-Object -First 1);Assert $layout.Count 'Unknown archive layout.'
  $parent=(Get-Item -LiteralPath $layout[0].fullPath).Parent.FullName
  return @(Get-ChildItem -LiteralPath $parent -File | Where-Object {$_.Extension.ToLowerInvariant() -in @('.dll','.ini','.json','.exe','.config')} | Sort-Object Name)
}
function Complete-ImportInspection($inspection,[string]$name,[string]$layoutPath,[string[]]$rootFileNames=@()){
  Assert ($name -and $name -notmatch '[\\/:*?"<>|]') 'Choose a valid, unique folder name.'
  $final=Join-Path $ModsRoot $name;Assert (!(Test-Path -LiteralPath $final)) "Folder already exists: $final"
  $layout=@($inspection.layouts | Where-Object {$_.fullPath -eq $layoutPath} | Select-Object -First 1);Assert $layout.Count 'Unknown archive layout.'
  try{
    New-Item -ItemType Directory -Path $final | Out-Null
    if($layout[0].kind -eq 'nativePC'){Copy-Item -LiteralPath $layout[0].fullPath -Destination (Join-Path $final 'nativePC') -Recurse}
    else{New-Item -ItemType Directory -Path (Join-Path $final 'nativePC') | Out-Null;foreach($child in @(Get-ChildItem -LiteralPath $layout[0].fullPath -Force)){Copy-Item -LiteralPath $child.FullName -Destination (Join-Path $final 'nativePC') -Recurse -Force}}
    $parent=(Get-Item -LiteralPath $layout[0].fullPath).Parent.FullName
    $available=@(Get-ImportRootFiles $inspection $layoutPath)
    $selected=@($available | Where-Object {$rootFileNames -contains $_.Name})
    if($selected.Count){$dest=Join-Path $final 'GameRoot';New-Item -ItemType Directory -Path $dest | Out-Null;foreach($f in $selected){Copy-Item -LiteralPath $f.FullName -Destination $dest}}
    foreach($file in @(Get-ChildItem -LiteralPath $parent -File | Where-Object {$_.Name -match '(?i)readme|install|require'})){Copy-Item -LiteralPath $file.FullName -Destination $final -ErrorAction SilentlyContinue}
    Write-ManagerLog 'info' "Imported mod archive" ([ordered]@{name=$name;archive=[IO.Path]::GetFileName($inspection.archive);layout=$layout[0].relative;rootFiles=@($selected.Name)})
    return $final
  }catch{if(Test-Path -LiteralPath $final){Remove-Item -LiteralPath $final -Recurse -Force};throw}
  finally{if(Test-Path -LiteralPath $inspection.stage){Remove-Item -LiteralPath $inspection.stage -Recurse -Force}}
}
function Cancel-ImportInspection($inspection){if($inspection -and $inspection.stage -and (Test-Path -LiteralPath $inspection.stage)){Remove-Item -LiteralPath $inspection.stage -Recurse -Force}}
