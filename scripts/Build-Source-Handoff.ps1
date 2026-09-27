param([string]$OutputPath)
$ErrorActionPreference='Stop'
$Root=(Resolve-Path (Join-Path $PSScriptRoot '..')).Path
& (Join-Path $PSScriptRoot 'Test-AgentHandoff.ps1') -Root $Root

$version=(Get-Content -Raw -Path (Join-Path $Root 'VERSION.txt')).Trim()
if([string]::IsNullOrWhiteSpace($OutputPath)){
    $OutputPath=Join-Path $Root ("MHW-Manual-Mod-Manager-v"+$version+"-Source-Handoff.zip")
}
$OutputPath=[System.IO.Path]::GetFullPath($OutputPath)
$shaPath=$OutputPath+'.sha256'
$temp=Join-Path ([System.IO.Path]::GetTempPath()) ('mhw-source-handoff-'+[guid]::NewGuid().ToString('N'))
$pendingZip=$OutputPath+'.tmp-'+[guid]::NewGuid().ToString('N')
New-Item -ItemType Directory -Force -Path $temp|Out-Null

$excludedTop=@('.git','.vs','artifacts','release','BuildLogs')
$excludedSegments=@('bin','obj')
try{
    $files=Get-ChildItem -Path $Root -File -Recurse -Force | Where-Object {
        $relative=$_.FullName.Substring($Root.Length).TrimStart([char[]]@([char]92, [char]47))
        $segments=$relative -split '[\\/]'
        if($segments.Count -gt 0 -and $excludedTop -contains $segments[0]){return $false}
        foreach($segment in $segments){if($excludedSegments -contains $segment){return $false}}
        if($_.FullName -eq $OutputPath -or $_.FullName -eq $shaPath -or $_.FullName -eq $pendingZip){return $false}
        if($relative.Replace([char]92,[char]47) -eq '_AGENT_CONTEXT/SOURCE_HANDOFF_MANIFEST.json'){return $false}
        if($segments.Count -eq 1 -and ($_.Name -like 'MHW-Manual-Mod-Manager-v*-Source-Handoff.zip' -or $_.Name -like 'MHW-Manual-Mod-Manager-v*-Source-Handoff.zip.sha256')){return $false}
        return $true
    }

    foreach($file in $files){
        $relative=$file.FullName.Substring($Root.Length).TrimStart([char[]]@([char]92, [char]47))
        $dest=Join-Path $temp $relative
        $dir=Split-Path -Parent $dest
        if(-not (Test-Path $dir)){New-Item -ItemType Directory -Force -Path $dir|Out-Null}
        Copy-Item -LiteralPath $file.FullName -Destination $dest -Force
    }

    $hashEntries=@()
    foreach($file in (Get-ChildItem -Path $temp -File -Recurse -Force | Sort-Object FullName)){
        $relative=$file.FullName.Substring($temp.Length).TrimStart([char[]]@([char]92, [char]47)).Replace([char]92, [char]47)
        $hash=(Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        $hashEntries += [pscustomobject]@{path=$relative;sha256=$hash;bytes=$file.Length}
    }
    $packageManifest=[ordered]@{
        formatVersion=1
        project='MHW Manual Mod Manager'
        sourceVersion=$version
        generatedAt=(Get-Date).ToString('o')
        continuityRequired=$true
        propagateToNextAgent=$true
        fileCount=$hashEntries.Count
        files=$hashEntries
    }
    $packageManifest | ConvertTo-Json -Depth 6 | Set-Content -Path (Join-Path $temp '_AGENT_CONTEXT\SOURCE_HANDOFF_MANIFEST.json') -Encoding UTF8

    # Compress-Archive/Get-ChildItem omit hidden files, including .verification on Unix.
    # Stage and validate the complete archive before replacing an existing handoff.
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [System.IO.Compression.ZipFile]::CreateFromDirectory($temp,$pendingZip,[System.IO.Compression.CompressionLevel]::Optimal,$false)
    $archive=[System.IO.Compression.ZipFile]::OpenRead($pendingZip)
    try{
        $names=@($archive.Entries | ForEach-Object {$_.FullName.Replace([char]92,[char]47)})
        foreach($entry in $hashEntries){
            if($names -cnotcontains $entry.path){throw "Source handoff omitted required payload: $($entry.path)"}
        }
        if($names.Count -ne $hashEntries.Count+1){throw 'Source handoff entry count does not match its manifest.'}
    }
    finally{$archive.Dispose()}
    # PowerShell marshals $null to an empty string for this .NET string overload.
    if(Test-Path $OutputPath){[System.IO.File]::Replace($pendingZip,$OutputPath,[NullString]::Value)}
    else{[System.IO.File]::Move($pendingZip,$OutputPath)}
    $zipHash=(Get-FileHash -LiteralPath $OutputPath -Algorithm SHA256).Hash.ToLowerInvariant()
    ($zipHash+'  '+[System.IO.Path]::GetFileName($OutputPath)) | Set-Content -Path $shaPath -Encoding ASCII
    Write-Host ("PASS: Source handoff created: "+$OutputPath) -ForegroundColor Green
    Write-Host ("SHA256: "+$zipHash) -ForegroundColor Green
}
finally{
    if(Test-Path $pendingZip){Remove-Item -LiteralPath $pendingZip -Force -ErrorAction SilentlyContinue}
    if(Test-Path $temp){Remove-Item -LiteralPath $temp -Recurse -Force -ErrorAction SilentlyContinue}
}
