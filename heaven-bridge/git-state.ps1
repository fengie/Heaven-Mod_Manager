function Get-HeavenBridgeGitCurrentBranch {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$Repository
    )

    $branchOutput = @(& git -C $Repository branch --show-current)
    if ($LASTEXITCODE -ne 0) {
        throw "Unable to read Git branch for Heaven Bridge repository: $Repository"
    }

    if ($branchOutput.Count -eq 0) {
        return $null
    }

    $branch = (($branchOutput | ForEach-Object { [string]$_ }) -join "`n").Trim()
    if ([string]::IsNullOrWhiteSpace($branch)) {
        return $null
    }

    return $branch
}
