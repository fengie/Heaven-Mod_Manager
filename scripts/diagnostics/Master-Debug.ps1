function Get-MhwMasterDebugPath {
    param([Parameter(Mandatory=$true)][string]$Root)
    return (Join-Path $Root 'MHW-DEBUG-ALL.log')
}

function Write-MhwMasterDebug {
    param(
        [Parameter(Mandatory=$true)][string]$Root,
        [Parameter(Mandatory=$true)][string]$Area,
        [Parameter(Mandatory=$true)][AllowEmptyString()][string]$Message
    )
    try {
        $path=Get-MhwMasterDebugPath -Root $Root
        $timestamp=(Get-Date).ToString('yyyy-MM-dd HH:mm:ss.fff zzz')
        $normalized=$Message -replace "`r`n","`n"
        $lines=$normalized -split "`n"
        foreach($line in $lines){
            [System.IO.File]::AppendAllText($path,("{0} [{1}] {2}{3}" -f $timestamp,$Area,$line,[Environment]::NewLine),[System.Text.UTF8Encoding]::new($false))
        }
    } catch {
        # Diagnostics must never become the reason the product/build fails.
    }
}

function Start-MhwMasterDebugSession {
    param(
        [Parameter(Mandatory=$true)][string]$Root,
        [Parameter(Mandatory=$true)][string]$Area,
        [Parameter(Mandatory=$true)][string]$Title
    )
    Write-MhwMasterDebug -Root $Root -Area $Area -Message ('=' * 100)
    Write-MhwMasterDebug -Root $Root -Area $Area -Message ("SESSION START: {0}" -f $Title)
    Write-MhwMasterDebug -Root $Root -Area $Area -Message ("Root: {0}" -f $Root)
    Write-MhwMasterDebug -Root $Root -Area $Area -Message ("PowerShell: {0}; PID: {1}; Machine: {2}" -f $PSVersionTable.PSVersion,$PID,[Environment]::MachineName)
}

function Stop-MhwMasterDebugSession {
    param(
        [Parameter(Mandatory=$true)][string]$Root,
        [Parameter(Mandatory=$true)][string]$Area,
        [Parameter(Mandatory=$true)][string]$Summary
    )
    Write-MhwMasterDebug -Root $Root -Area $Area -Message ("SESSION END: {0}" -f $Summary)
    Write-MhwMasterDebug -Root $Root -Area $Area -Message ('=' * 100)
}
