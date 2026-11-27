[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)]
    [ValidatePattern('\A[a-z][a-z0-9-]{0,63}\z')][string]$Stage,
    [Parameter(Mandatory=$true)]
    [ValidateSet('powershell.exe','dotnet','git','gh')][string]$Program,
    [Parameter(Mandatory=$true)][string[]]$ArgumentList
)

$ErrorActionPreference = 'Stop'
try {
    $runnerTemp = Get-Item -LiteralPath $env:RUNNER_TEMP -Force
    if (-not $runnerTemp.PSIsContainer -or ($runnerTemp.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
        throw 'Invalid runner temporary root.'
    }
    # Unpredictable direct child: no workspace artifact pattern or stale receipt can include it.
    $captureDir = New-Item -ItemType Directory -Path (
        Join-Path $runnerTemp.FullName ('mhw-private-ci-' + [guid]::NewGuid().ToString('N')))
    $capturePath = Join-Path $captureDir.FullName 'diagnostics.log'
    # Windows PowerShell 5.1 maps native stderr to its error stream. Capture both
    # output and error without turning harmless stderr into a different gate result.
    $exe = (Get-Command -Name $Program -CommandType Application -ErrorAction Stop | Select-Object -First 1).Source
    $ErrorActionPreference = 'Continue'
    $global:LASTEXITCODE = -1
    & $exe @ArgumentList *> $capturePath
    $childExit = $LASTEXITCODE
    $ErrorActionPreference = 'Stop'
    if ($null -eq $childExit -or $childExit -ne 0) { throw 'Child failed.' }
}
catch {
    # Never turn an exception message, command arguments, runner path, or native
    # output into public console text. Gate state remains failed on a real failure.
    throw "MHW CI stage '$Stage' failed. Raw diagnostics were not published."
}
Write-Host "MHW CI stage '$Stage' passed."
