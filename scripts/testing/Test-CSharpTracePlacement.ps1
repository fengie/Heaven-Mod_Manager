param([switch]$Quiet)
$ErrorActionPreference='Stop'
$Root=(Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
. (Join-Path $PSScriptRoot '..\diagnostics\Master-Debug.ps1')
$errors=New-Object System.Collections.Generic.List[string]
$files=@(Get-ChildItem (Join-Path $Root 'src') -Filter '*.cs' -File -Recurse | Sort-Object FullName)
foreach($file in $files){
  $lines=@(Get-Content -LiteralPath $file.FullName)
  for($i=0;$i -lt $lines.Count;$i++){
    if($lines[$i] -notmatch 'using\s+var\s+__mhwTrace\s*=\s*MasterDebugLog\.BeginMethod\(\);'){continue}
    $j=$i-1
    while($j -ge 0 -and [string]::IsNullOrWhiteSpace($lines[$j])){$j--}
    if($j -lt 0 -or $lines[$j].Trim() -ne '{'){continue}
    $k=$j-1
    while($k -ge 0 -and [string]::IsNullOrWhiteSpace($lines[$k])){$k--}
    if($k -lt 0){continue}
    $header=$lines[$k].Trim()
    if($header -match '=\s*new\b'){
      $relative=$file.FullName.Substring($Root.Length).TrimStart([char[]]@('\','/'))
      $errors.Add(('{0}:{1}: method trace was inserted inside an object/collection initializer. Header: {2}' -f $relative,($i+1),$header))
    }
  }
}
if($errors.Count -gt 0){
  foreach($error in $errors){Write-Host $error -ForegroundColor Red;Write-MhwMasterDebug -Root $Root -Area 'CS-TRACE-PREFLIGHT' -Message $error}
  Write-MhwMasterDebug -Root $Root -Area 'CS-TRACE-PREFLIGHT' -Message ("FAIL: invalid trace placement count="+$errors.Count)
  exit 4
}
if(-not $Quiet){Write-Host ("C# trace placement preflight PASS for "+$files.Count+" source file(s).") -ForegroundColor Green}
Write-MhwMasterDebug -Root $Root -Area 'CS-TRACE-PREFLIGHT' -Message ("PASS: checked "+$files.Count+" source file(s).")
exit 0
