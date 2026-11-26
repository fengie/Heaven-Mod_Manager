[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$InventoryPath,
    [Parameter(Mandatory=$true)][string]$EvidenceDirectory
)
$ErrorActionPreference = 'Stop'

# A diagnostic inventory, not publication authority. Keep missing and invalid
# evidence distinct from an observed failure and never infer a green result.
# Windows PowerShell 5.1 can emit a JSON root array as one pipeline object.
# Assign directly and let foreach enumerate the actual root array.
$inventory = ConvertFrom-Json -InputObject (Get-Content -LiteralPath $InventoryPath -Raw -Encoding UTF8)
$expected = New-Object 'System.Collections.Generic.HashSet[string]' -ArgumentList ([StringComparer]::OrdinalIgnoreCase)
$ids = @()
foreach ($profile in $inventory) {
    $id = [string]$profile.id
    if ([string]::IsNullOrWhiteSpace($id) -or $id.IndexOfAny([IO.Path]::GetInvalidFileNameChars()) -ge 0 -or
        $id -in @('.','..') -or !$expected.Add($id)) {
        throw "Invalid or duplicate fixture profile identity: $id"
    }
    $ids += $id
}
if ($ids.Count -eq 0) { throw 'Fixture inventory is empty.' }
$rows = New-Object 'System.Collections.Generic.List[object]'
$unexpected = New-Object 'System.Collections.Generic.List[string]'
foreach ($scenario in @('game','repair')) {
    foreach ($id in $ids) {
        $name = "$scenario-$id.json"
        $file = Join-Path $EvidenceDirectory $name
        $status = 'NotRun'
        $lastSuccess = ''
        $problem = ''
        if (Test-Path -LiteralPath $file -PathType Leaf) {
            try {
                $receipt = Get-Content -LiteralPath $file -Raw -Encoding UTF8 | ConvertFrom-Json
                if ($null -eq $receipt -or [string]$receipt.Id -cne $id -or
                    $null -eq $receipt.PSObject.Properties['error'] -or
                    $null -eq $receipt.PSObject.Properties['passed'] -or
                    $receipt.passed -isnot [array]) {
                    throw 'Missing or mismatched receipt identity, explicit outcome, or completed operations.'
                }
                if ($null -ne $receipt.error -and $receipt.error -isnot [string]) {
                    throw 'Invalid receipt outcome type.'
                }
                $completed = @($receipt.passed)
                foreach ($operation in $completed) {
                    if ($operation -isnot [string] -or [string]::IsNullOrWhiteSpace($operation)) {
                        throw 'Invalid completed operation.'
                    }
                }
                if ($completed.Count -gt 0) { $lastSuccess = [string]$completed[-1] }
                if ($null -ne $receipt.error) {
                    if ([string]::IsNullOrWhiteSpace($receipt.error)) { throw 'Empty non-null error is ambiguous.' }
                    $status = 'Failed'
                    $problem = [string]$receipt.error
                }
                elseif ($completed.Count -eq 0) { throw 'Passing receipt contains no completed operations.' }
                else { $status = 'Passed' }
            }
            catch {
                $status = 'Invalid'
                $problem = $_.Exception.Message
            }
        }
        elseif (Test-Path -LiteralPath $file) {
            $status = 'Invalid'
            $problem = 'Receipt path is not a regular file.'
        }
        $rows.Add([pscustomobject]@{
            scenario=$scenario; profileId=$id; status=$status; lastSuccess=$lastSuccess; error=$problem
        })
    }
    if (Test-Path -LiteralPath $EvidenceDirectory -PathType Container) {
        foreach ($file in @(Get-ChildItem -LiteralPath $EvidenceDirectory -Filter "$scenario-*.json" -File)) {
            $fileId = $file.BaseName.Substring($scenario.Length + 1)
            if (!$expected.Contains($fileId)) { $unexpected.Add($file.Name) }
        }
    }
}
$passedCount = @($rows | Where-Object { $_.status -eq 'Passed' }).Count
$failedCount = @($rows | Where-Object { $_.status -eq 'Failed' }).Count
$notRunCount = @($rows | Where-Object { $_.status -eq 'NotRun' }).Count
$invalidCount = @($rows | Where-Object { $_.status -eq 'Invalid' }).Count
[pscustomobject]@{
    schemaVersion=1
    expectedCount=$rows.Count
    passedCount=$passedCount
    failedCount=$failedCount
    notRunCount=$notRunCount
    invalidCount=$invalidCount
    unexpectedCount=$unexpected.Count
    complete=($passedCount -eq $rows.Count -and $unexpected.Count -eq 0)
    profiles=$rows.ToArray()
    unexpectedReceipts=$unexpected.ToArray()
}
