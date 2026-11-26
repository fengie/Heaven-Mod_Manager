$ErrorActionPreference = 'Stop'
$summaryScript = Join-Path $PSScriptRoot 'Get-InstalledWpfReceiptSummary.ps1'
$root = Join-Path ([IO.Path]::GetTempPath()) ('mhw-wpf-receipts-test-' + [Guid]::NewGuid().ToString('N'))
try {
    New-Item -ItemType Directory -Path $root | Out-Null
    $inventory = Join-Path $root 'inventory.json'
    $receipts = Join-Path $root 'receipts'
    New-Item -ItemType Directory -Path $receipts | Out-Null
    @(@{id='alpha'}, @{id='beta'}) | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $inventory -Encoding UTF8
    function Write-FixtureReceipt([string]$Scenario,[string]$Id,[string]$ErrorText) {
        $message = if ($ErrorText) { $ErrorText } else { $null }
        @{Id=$Id; passed=@('startup','deployment'); error=$message} |
            ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $receipts "$Scenario-$Id.json") -Encoding UTF8
    }

    Write-FixtureReceipt 'game' 'alpha' ''
    Write-FixtureReceipt 'game' 'beta' 'focus timeout'
    Write-FixtureReceipt 'repair' 'alpha' ''
    $partial = & $summaryScript -InventoryPath $inventory -EvidenceDirectory $receipts
    if ($partial.complete -or $partial.expectedCount -ne 4 -or $partial.passedCount -ne 2 -or
        $partial.failedCount -ne 1 -or $partial.notRunCount -ne 1 -or $partial.invalidCount -ne 0) {
        throw 'Partial matrix counts do not preserve failure versus missing evidence.'
    }
    $failed = @($partial.profiles | Where-Object { $_.scenario -eq 'game' -and $_.profileId -eq 'beta' })
    if ($failed.Count -ne 1 -or $failed[0].lastSuccess -ne 'deployment' -or $failed[0].error -ne 'focus timeout') {
        throw 'Failure diagnostics lost the precise profile and last successful operation.'
    }

    Write-FixtureReceipt 'game' 'beta' ''
    Write-FixtureReceipt 'repair' 'beta' ''
    $complete = & $summaryScript -InventoryPath $inventory -EvidenceDirectory $receipts
    if (!$complete.complete -or $complete.passedCount -ne 4) { throw 'Complete, exact fixture evidence was not recognized.' }

    # File names cannot stand in for missing or mismatched receipt identities.
    @{Id='alpha'; passed=@('startup'); error=$null} | ConvertTo-Json -Depth 4 |
        Set-Content -LiteralPath (Join-Path $receipts 'game-beta.json') -Encoding UTF8
    $wrong = & $summaryScript -InventoryPath $inventory -EvidenceDirectory $receipts
    if ($wrong.complete -or $wrong.invalidCount -ne 1 -or $wrong.passedCount -ne 3) {
        throw 'Mismatched receipt identity was credited.'
    }
    Write-FixtureReceipt 'game' 'beta' ''
    @{Id='beta'; passed=@('startup')} | ConvertTo-Json |
        Set-Content -LiteralPath (Join-Path $receipts 'repair-beta.json') -Encoding UTF8
    $missingOutcome = & $summaryScript -InventoryPath $inventory -EvidenceDirectory $receipts
    if ($missingOutcome.complete -or $missingOutcome.invalidCount -ne 1) { throw 'Missing explicit outcome was credited.' }
    Write-FixtureReceipt 'repair' 'beta' ''
    '{broken json' | Set-Content -LiteralPath (Join-Path $receipts 'game-beta.json') -Encoding UTF8
    $malformed = & $summaryScript -InventoryPath $inventory -EvidenceDirectory $receipts
    if ($malformed.complete -or $malformed.invalidCount -ne 1) { throw 'Malformed receipt was credited.' }
    Write-FixtureReceipt 'game' 'beta' ''
    Write-FixtureReceipt 'repair' 'unexpected' ''
    $unexpected = & $summaryScript -InventoryPath $inventory -EvidenceDirectory $receipts
    if ($unexpected.complete -or $unexpected.unexpectedCount -ne 1 -or $unexpected.passedCount -ne 4) {
        throw 'Unexpected profile evidence was silently accepted.'
    }
    @(@{id='alpha'}, @{id='alpha'}) | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $inventory -Encoding UTF8
    $rejected = $false
    try { & $summaryScript -InventoryPath $inventory -EvidenceDirectory $receipts | Out-Null } catch { $rejected = $true }
    if (!$rejected) { throw 'Duplicate registry identity was accepted.' }

    Write-Host 'PASS: Installed WPF matrix diagnostics distinguish passed, failed, invalid, not-run and unexpected receipts.'
}
finally {
    if (Test-Path -LiteralPath $root) { Remove-Item -LiteralPath $root -Recurse -Force }
}
