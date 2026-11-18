# Parse explicit application-measured startup UI phase durations.
# This file is dot-sourced by Measure-StartupPerformance.ps1 and its negative fixtures.
# Missing/invalid evidence is a failed benchmark, never a zero or null duration.

function Read-StartupUiPhaseMs {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$LogPath,
        [Parameter(Mandatory = $true)]
        [ValidateSet('reload-mods', 'conflict-analysis')]
        [string]$Phase
    )

    if (-not (Test-Path -LiteralPath $LogPath -PathType Leaf)) {
        throw "Missing startup UI phase '$Phase': master log does not exist."
    }
    $logText = Get-Content -LiteralPath $LogPath -Raw -ErrorAction Stop
    $pattern = '\[STARTUP-UI-PHASE\][^\r\n]*phase=' +
        [regex]::Escape($Phase) + '; elapsedMs=([0-9]+(?:\.[0-9]+)?)'
    $matches = [regex]::Matches($logText, $pattern)
    if ($matches.Count -eq 0) {
        throw "Missing startup UI phase '$Phase': no measured completion entry."
    }
    $last = $matches[$matches.Count - 1].Groups[1].Value
    $value = [double]::Parse(
        $last,
        [System.Globalization.NumberStyles]::Float,
        [System.Globalization.CultureInfo]::InvariantCulture)
    if ([double]::IsNaN($value) -or [double]::IsInfinity($value) -or $value -lt 0) {
        throw "Missing startup UI phase '$Phase': invalid elapsed duration."
    }
    return $value
}
