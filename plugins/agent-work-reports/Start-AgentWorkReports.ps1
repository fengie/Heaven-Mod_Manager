param(
    [int]$Port = 7341
)

$ErrorActionPreference = 'Stop'
$Root = Split-Path -Parent $MyInvocation.MyCommand.Path
$Url = "http://127.0.0.1:$Port"

function Test-ReportsHealth {
    try {
        $response = Invoke-RestMethod -Uri "$Url/api/health" -Method Get -TimeoutSec 1
        return $response.ok -eq $true -and $response.app -eq 'agent-work-reports'
    }
    catch {
        return $false
    }
}

if (-not (Test-ReportsHealth)) {
    $env:AGENT_WORK_REPORTS_PORT = [string]$Port
    Start-Process -FilePath 'node' -ArgumentList 'server.mjs' -WorkingDirectory $Root -WindowStyle Hidden | Out-Null
    $deadline = (Get-Date).AddSeconds(8)
    do {
        Start-Sleep -Milliseconds 200
        if (Test-ReportsHealth) { break }
    } while ((Get-Date) -lt $deadline)
}

if (-not (Test-ReportsHealth)) {
    throw "Agent Work Reports did not become healthy at $Url. Run 'node server.mjs' from $Root for diagnostics."
}

Start-Process $Url | Out-Null
