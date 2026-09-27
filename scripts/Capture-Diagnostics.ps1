param(
    [int]$ProcessId = 0,
    [ValidateRange(5,120)][int]$DurationSeconds = 20,
    [string]$OutputDirectory = ''
)
$ErrorActionPreference='Continue'
$Root=(Resolve-Path (Join-Path $PSScriptRoot '..')).Path
. (Join-Path $PSScriptRoot 'Master-Debug.ps1')
Start-MhwMasterDebugSession -Root $Root -Area 'CAPTURE' -Title 'Runtime diagnostics capture'
Write-MhwMasterDebug -Root $Root -Area 'CAPTURE' -Message ("Requested ProcessId=$ProcessId; DurationSeconds=$DurationSeconds; OutputDirectory=$OutputDirectory")
if($ProcessId -le 0){
    $candidates=@(Get-Process -ErrorAction SilentlyContinue | Where-Object { $_.ProcessName -like '*MHW*Mod*Manager*' -or $_.MainWindowTitle -like '*MHW*Mod*Manager*' })
    if($candidates.Count -ne 1){
        Write-Host 'Running candidate processes:'
        $candidates | Select-Object Id,ProcessName,MainWindowTitle | Format-Table
        throw 'Pass -ProcessId explicitly when exactly one manager process cannot be identified.'
    }
    $ProcessId=$candidates[0].Id
}
if([string]::IsNullOrWhiteSpace($OutputDirectory)){
    $OutputDirectory=Join-Path $Root ('diagnostics\capture-'+(Get-Date -Format 'yyyyMMdd-HHmmss')+'-pid'+$ProcessId)
}
New-Item -ItemType Directory -Force $OutputDirectory | Out-Null

$meta=[ordered]@{
    capturedUtc=(Get-Date).ToUniversalTime().ToString('O')
    processId=$ProcessId
    durationSeconds=$DurationSeconds
    windows=[Environment]::OSVersion.VersionString
    powershell=$PSVersionTable.PSVersion.ToString()
}
$meta | ConvertTo-Json | Set-Content (Join-Path $OutputDirectory 'capture.json') -Encoding utf8

function Invoke-Tool([string]$Name,[scriptblock]$Body){
    if(Get-Command $Name -ErrorAction SilentlyContinue){
        Write-Host "Collecting with $Name..." -ForegroundColor Cyan
        Write-MhwMasterDebug -Root $Root -Area 'CAPTURE-STAGE' -Message ("START: "+$Name)
        try{& $Body;Write-MhwMasterDebug -Root $Root -Area 'CAPTURE-STAGE' -Message ("PASS: "+$Name)}catch{Write-MhwMasterDebug -Root $Root -Area 'CAPTURE-STAGE' -Message ("FAIL: "+$Name+" :: "+$_.Exception.ToString());$_ | Out-String | Set-Content (Join-Path $OutputDirectory ($Name+'.error.txt')) -Encoding utf8}
    }else{
        Write-MhwMasterDebug -Root $Root -Area 'CAPTURE-STAGE' -Message ("SKIP: "+$Name+" is not installed")
        "$Name is not installed. Install with: dotnet tool install --global $Name" | Set-Content (Join-Path $OutputDirectory ($Name+'.missing.txt')) -Encoding utf8
    }
}

$duration=[TimeSpan]::FromSeconds($DurationSeconds).ToString('hh\:mm\:ss')
Invoke-Tool 'dotnet-stack' { dotnet-stack report --process-id $ProcessId 2>&1 | Set-Content (Join-Path $OutputDirectory 'managed-stack.txt') -Encoding utf8 }
Invoke-Tool 'dotnet-counters' { dotnet-counters collect --process-id $ProcessId --duration $duration --format csv --output (Join-Path $OutputDirectory 'runtime-counters.csv') 2>&1 | Set-Content (Join-Path $OutputDirectory 'dotnet-counters.txt') -Encoding utf8 }
Invoke-Tool 'dotnet-trace' { dotnet-trace collect --process-id $ProcessId --duration $duration --output (Join-Path $OutputDirectory 'hang.nettrace') 2>&1 | Set-Content (Join-Path $OutputDirectory 'dotnet-trace.txt') -Encoding utf8 }
Invoke-Tool 'dotnet-gcdump' { dotnet-gcdump collect --process-id $ProcessId --output (Join-Path $OutputDirectory 'heap.gcdump') 2>&1 | Set-Content (Join-Path $OutputDirectory 'dotnet-gcdump.txt') -Encoding utf8 }

Get-Process -Id $ProcessId -ErrorAction SilentlyContinue | Select-Object Id,ProcessName,CPU,WorkingSet64,PrivateMemorySize64,Threads,Handles,StartTime | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $OutputDirectory 'process.json') -Encoding utf8
foreach($textArtifact in @(Get-ChildItem $OutputDirectory -File -ErrorAction SilentlyContinue | Where-Object { $_.Extension -in '.txt','.json','.csv' } | Sort-Object Name)){
    Write-MhwMasterDebug -Root $Root -Area 'CAPTURE-FILE' -Message ("BEGIN " + $textArtifact.FullName)
    try{Get-Content $textArtifact.FullName -ErrorAction Stop | ForEach-Object{Write-MhwMasterDebug -Root $Root -Area 'CAPTURE-DATA' -Message $_.ToString()}}catch{Write-MhwMasterDebug -Root $Root -Area 'CAPTURE-FILE' -Message ("Could not merge textual artifact: "+$_.Exception.Message)}
    Write-MhwMasterDebug -Root $Root -Area 'CAPTURE-FILE' -Message ("END " + $textArtifact.FullName)
}
Write-MhwMasterDebug -Root $Root -Area 'CAPTURE' -Message 'Binary trace/gcdump artifacts cannot be embedded as text; their paths are retained above.'
Write-Host "Diagnostics captured to: $OutputDirectory" -ForegroundColor Green
Write-Warning 'A gcdump/trace can expose filenames and application state. Review diagnostic files before sharing them.'
Write-MhwMasterDebug -Root $Root -Area 'CAPTURE' -Message ("Diagnostics captured to: $OutputDirectory")
Stop-MhwMasterDebugSession -Root $Root -Area 'CAPTURE' -Summary 'COMPLETE'
