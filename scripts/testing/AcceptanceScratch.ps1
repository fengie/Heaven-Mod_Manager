# Disposable verification artifacts belong here. Keep durable evidence outside scratch.
function Get-MhwAcceptanceTempRoot {
    param([string]$ScratchRoot = [IO.Path]::GetTempPath())
    $root = [IO.Path]::GetFullPath($ScratchRoot)
    if ($root -ne [IO.Path]::GetPathRoot($root)) {
        $root = $root.TrimEnd('\', '/')
    }
    $directory = Get-Item -LiteralPath $root -Force -ErrorAction Stop
    if (!$directory.PSIsContainer) { throw 'Acceptance scratch root is not a directory.' }
    for ($item = $directory; $null -ne $item; $item = $item.Parent) {
        if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) {
            throw 'Acceptance temp root traverses a reparse point.'
        }
    }
    return $root
}

function Assert-MhwAcceptanceScratchTree([string]$Path, [string]$ScratchRoot = [IO.Path]::GetTempPath()) {
    $root = Get-MhwAcceptanceTempRoot -ScratchRoot $ScratchRoot
    if ([IO.Path]::GetFullPath($Path) -cne $Path -or
        [IO.Path]::GetDirectoryName($Path) -ine $root -or
        [IO.Path]::GetFileName($Path) -cnotmatch '^mhw-acceptance-[a-f0-9]{32}$') {
        throw 'Unexpected acceptance scratch path.'
    }
    $pending = [Collections.Generic.Stack[string]]::new()
    $pending.Push($Path)
    while ($pending.Count) {
        $item = Get-Item -LiteralPath $pending.Pop() -Force -ErrorAction Stop
        if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) {
            throw 'Acceptance scratch contains a reparse point.'
        }
        if ($item.PSIsContainer) {
            foreach ($child in Get-ChildItem -LiteralPath $item.FullName -Force -ErrorAction Stop) {
                $pending.Push($child.FullName)
            }
        }
    }
}

function New-MhwAcceptanceScratch {
    param([switch]$ReclaimAfterProducerExit, [string]$ScratchRoot = [IO.Path]::GetTempPath())
    $root = Get-MhwAcceptanceTempRoot -ScratchRoot $ScratchRoot
    $token = [Guid]::NewGuid().ToString('N')
    $path = Join-Path $root ('mhw-acceptance-' + $token)
    $process = [Diagnostics.Process]::GetCurrentProcess()
    try {
        $marker = [ordered]@{
            schema = 'mhw-test-scratch/v1'
            owner = 'MhwAcceptanceScratch'
            reclaimAfterProducerExit = $ReclaimAfterProducerExit.IsPresent
            token = $token
            processId = $process.Id
            processStartUtc = $process.StartTime.ToUniversalTime().ToString('o')
            createdUtc = [DateTime]::UtcNow.ToString('o')
        } | ConvertTo-Json -Compress
    } finally {
        $process.Dispose()
    }
    New-Item -ItemType Directory -Path $path -ErrorAction Stop | Out-Null
    [IO.File]::WriteAllText((Join-Path $path '.mhw-test-scratch.json'), $marker)
    return @{ Path = $path; Token = $token }
}

function Remove-MhwAcceptanceScratch($Scratch, [string]$ScratchRoot = [IO.Path]::GetTempPath()) {
    if (!(Test-Path -LiteralPath $Scratch.Path)) { return }
    Assert-MhwAcceptanceScratchTree $Scratch.Path -ScratchRoot $ScratchRoot
    $markerPath = Join-Path $Scratch.Path '.mhw-test-scratch.json'
    $marker = Get-Content -LiteralPath $markerPath -Raw -ErrorAction Stop | ConvertFrom-Json
    if ($marker.schema -cne 'mhw-test-scratch/v1' -or $marker.owner -cne 'MhwAcceptanceScratch' -or
        $marker.token -cne $Scratch.Token -or $marker.processId -ne $PID -or
        [IO.Path]::GetFileName($Scratch.Path) -cne ('mhw-acceptance-' + $marker.token)) {
        throw 'Acceptance scratch marker identity mismatch.'
    }
    Remove-Item -LiteralPath $Scratch.Path -Recurse -Force -ErrorAction Stop
}
