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

# StartupDiagnosticSession atomically replaces the report after each phase.
function Read-StartupReportText([string]$Path) {
    $share = [IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete
    $stream = [IO.File]::Open($Path, [IO.FileMode]::Open, [IO.FileAccess]::Read, $share)
    try {
        $reader = [IO.StreamReader]::new($stream, [Text.Encoding]::UTF8, $true)
        try { $reader.ReadToEnd() } finally { $reader.Dispose() }
    } finally { $stream.Dispose() }
}
