param(
    [int]$ColdIterations = 3,
    [int]$WarmIterations = 3,
    [int]$ModCount = 80,
    [int]$FilesPerMod = 25,
    [int]$TimeoutSeconds = 120
)

$ErrorActionPreference = 'Stop'
$Root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$BuildLogs = Join-Path $Root 'BuildLogs'
New-Item -ItemType Directory -Force -Path $BuildLogs | Out-Null

function Get-AppExe {
    $candidate = Get-ChildItem -LiteralPath (Join-Path $Root 'src\MhwModManager.App\bin\Release') -Filter 'MHW Mod Manager.exe' -File -Recurse |
        Where-Object { $_.FullName -notmatch '\\ref\\|\\publish\\' } |
        Sort-Object FullName |
        Select-Object -First 1
    if (-not $candidate) { throw 'Could not locate built MHW Mod Manager.exe under the Release output.' }
    return $candidate.FullName
}

function New-GameFixture([string]$RootPath) {
    New-Item -ItemType Directory -Force -Path $RootPath | Out-Null
    $exe = Join-Path $RootPath 'MonsterHunterWorld.exe'
    if (-not (Test-Path -LiteralPath $exe)) {
        [IO.File]::WriteAllBytes($exe, [byte[]](0..255))
    }
    New-Item -ItemType Directory -Force -Path (Join-Path $RootPath 'nativePC') | Out-Null
}

function New-ModFixture([string]$ModsRoot) {
    New-Item -ItemType Directory -Force -Path $ModsRoot | Out-Null
    for ($m = 0; $m -lt $ModCount; $m++) {
        $leaf = Join-Path $ModsRoot ('PerfMod{0:D4}\nativePC\pl\fixture\m{0:D4}' -f $m)
        New-Item -ItemType Directory -Force -Path $leaf | Out-Null
        for ($f = 0; $f -lt $FilesPerMod; $f++) {
            $path = Join-Path $leaf ('file{0:D4}.tex' -f $f)
            $bytes = New-Object byte[] 4096
            for ($i = 0; $i -lt $bytes.Length; $i += 256) {
                $bytes[$i] = [byte](($m + $f + $i) % 251)
            }
            [IO.File]::WriteAllBytes($path, $bytes)
        }
    }
}

function Wait-StartupReport([string]$ToolRoot, [Diagnostics.Process]$Process, [int]$Timeout) {
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($Timeout)
    while ([DateTimeOffset]::UtcNow -lt $deadline) {
        $Process.Refresh()
        if ($Process.HasExited) {
            throw "Application exited before startup completed. ExitCode=$($Process.ExitCode)"
        }
        $report = Get-ChildItem -LiteralPath (Join-Path $ToolRoot 'StartupLogs') -Filter 'startup-*.json' -File -ErrorAction SilentlyContinue |
            Sort-Object LastWriteTimeUtc -Descending |
            Select-Object -First 1
        if ($report) {
            try {
                $json = Get-Content -LiteralPath $report.FullName -Raw | ConvertFrom-Json
                if ($json.overall -eq 'PASS' -or $json.overall -eq 'FAIL') {
                    return [pscustomobject]@{ Path = $report.FullName; Report = $json }
                }
            } catch {
                # Tolerate a transient read race while the diagnostic report is replaced.
            }
        }
        Start-Sleep -Milliseconds 200
    }
    throw "Timed out after $Timeout seconds waiting for a completed startup diagnostic report."
}

function Get-StageMs($Report, [string]$Name) {
    $entry = @($Report.entries | Where-Object { $_.name -eq $Name } | Select-Object -Last 1)
    if ($entry.Count -eq 0) { return $null }
    return [double]$entry[0].milliseconds
}

function Get-ToStageMs($Report, [string]$Name) {
    $entry = @($Report.entries | Where-Object { $_.name -eq $Name } | Select-Object -Last 1)
    if ($entry.Count -eq 0) { return $null }
    $start = [DateTimeOffset]::Parse([string]$Report.startedAt)
    $end = [DateTimeOffset]::Parse([string]$entry[0].endedAt)
    return ($end - $start).TotalMilliseconds
}

function Invoke-StartupRun([string]$Kind, [int]$Iteration, [string]$ToolRoot, [string]$GameRoot, [string]$Exe) {
    $logRoot = Join-Path $ToolRoot 'StartupLogs'
    if (Test-Path -LiteralPath $logRoot) { Remove-Item -LiteralPath $logRoot -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $logRoot | Out-Null

    $oldHome = $env:MOD_MANAGER_HOME
    $oldGame = $env:MOD_MANAGER_GAME_ROOT
    $oldDebug = $env:MOD_MANAGER_DEBUG_ROOT
    $env:MOD_MANAGER_HOME = $ToolRoot
    $env:MOD_MANAGER_GAME_ROOT = $GameRoot
    $env:MOD_MANAGER_DEBUG_ROOT = $ToolRoot

    $sw = [Diagnostics.Stopwatch]::StartNew()
    $process = $null
    try {
        $process = Start-Process -FilePath $Exe -WorkingDirectory (Split-Path -Parent $Exe) -PassThru
        $completed = Wait-StartupReport -ToolRoot $ToolRoot -Process $process -Timeout $TimeoutSeconds
        $sw.Stop()
        Start-Sleep -Milliseconds 750
        $process.Refresh()

        $report = $completed.Report
        if ($report.overall -ne 'PASS') {
            throw "Startup report failed: $($report.summary)"
        }

        return [pscustomobject][ordered]@{
            kind = $Kind
            iteration = $Iteration
            process_to_ready_ms = [math]::Round($sw.Elapsed.TotalMilliseconds, 1)
            session_to_main_window_show_ms = [math]::Round((Get-ToStageMs $report 'ui.main-window.show'), 1)
            session_to_ready_ms = [math]::Round((([DateTimeOffset]::Parse([string]$report.generatedAt)) - ([DateTimeOffset]::Parse([string]$report.startedAt))).TotalMilliseconds, 1)
            cpu_ms = [math]::Round($process.TotalProcessorTime.TotalMilliseconds, 1)
            peak_working_set_mb = [math]::Round($process.PeakWorkingSet64 / 1MB, 1)
            idle_working_set_mb = [math]::Round($process.WorkingSet64 / 1MB, 1)
            catalog_refresh_ms = Get-StageMs $report 'startup.catalog.refresh'
            armor_import_ms = Get-StageMs $report 'startup.armor-index.import'
            nexus_refresh_ms = Get-StageMs $report 'startup.intelligence.nexus'
            game_build_ms = Get-StageMs $report 'startup.intelligence.game-build'
            recovery_ms = Get-StageMs $report 'startup.recovery'
            automation_ms = Get-StageMs $report 'startup.automation'
            main_window_initialize_ms = Get-StageMs $report 'ui.main-window.initialize'
            report = $completed.Path
        }
    }
    finally {
        if ($process -and -not $process.HasExited) {
            Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
            try { [void]$process.WaitForExit(5000) } catch {}
        }
        $env:MOD_MANAGER_HOME = $oldHome
        $env:MOD_MANAGER_GAME_ROOT = $oldGame
        $env:MOD_MANAGER_DEBUG_ROOT = $oldDebug
    }
}

function Get-Median([object[]]$Values) {
    $vals = @($Values | Where-Object { $_ -ne $null } | ForEach-Object { [double]$_ } | Sort-Object)
    if ($vals.Count -eq 0) { return $null }
    if (($vals.Count % 2) -eq 1) { return $vals[[int][math]::Floor($vals.Count / 2)] }
    $i = $vals.Count / 2
    return ($vals[$i - 1] + $vals[$i]) / 2
}

$appExe = Get-AppExe
$appDir = Split-Path -Parent $appExe
$packageBytes = (Get-ChildItem -LiteralPath $appDir -File -Recurse | Measure-Object -Property Length -Sum).Sum

$fixtureRoot = Join-Path $env:RUNNER_TEMP ('mhw-startup-perf-' + [Guid]::NewGuid().ToString('N'))
$gameRoot = Join-Path $fixtureRoot 'game'
$sourceMods = Join-Path $fixtureRoot 'fixture-mods'
New-Item -ItemType Directory -Force -Path $fixtureRoot | Out-Null
New-GameFixture $gameRoot
New-ModFixture $sourceMods

$runs = New-Object System.Collections.Generic.List[object]
$warmRoot = $null
try {
    for ($i = 1; $i -le $ColdIterations; $i++) {
        $toolRoot = Join-Path $fixtureRoot ("cold-$i")
        New-Item -ItemType Directory -Force -Path $toolRoot | Out-Null
        Copy-Item -LiteralPath $sourceMods -Destination (Join-Path $toolRoot 'Mods') -Recurse
        $run = Invoke-StartupRun -Kind 'cold-state' -Iteration $i -ToolRoot $toolRoot -GameRoot $gameRoot -Exe $appExe
        [void]$runs.Add($run)
        Write-Host ('STARTUP_PERF_RUN=' + ($run | ConvertTo-Json -Compress))
        $warmRoot = $toolRoot
    }

    if (-not $warmRoot) { throw 'At least one cold iteration is required to seed warm-state measurements.' }

    for ($i = 1; $i -le $WarmIterations; $i++) {
        $run = Invoke-StartupRun -Kind 'warm-state' -Iteration $i -ToolRoot $warmRoot -GameRoot $gameRoot -Exe $appExe
        $runs.Add($run)
        Write-Host ('STARTUP_PERF_RUN=' + ($run | ConvertTo-Json -Compress))
    }

    $metricNames = @(
        'process_to_ready_ms',
        'session_to_main_window_show_ms',
        'session_to_ready_ms',
        'cpu_ms',
        'peak_working_set_mb',
        'idle_working_set_mb',
        'catalog_refresh_ms',
        'armor_import_ms',
        'nexus_refresh_ms',
        'game_build_ms',
        'recovery_ms',
        'automation_ms',
        'main_window_initialize_ms'
    )

    $summary = [ordered]@{
        source_sha = $env:GITHUB_SHA
        mod_count = $ModCount
        files_per_mod = $FilesPerMod
        total_mod_files = $ModCount * $FilesPerMod
        package_size_mb = [math]::Round($packageBytes / 1MB, 1)
        cold = [ordered]@{}
        warm = [ordered]@{}
        runs = $runs.ToArray()
        note = 'cold-state resets manager state/database but does not claim a flushed Windows filesystem cache; warm-state reuses the same indexed manager state.'
    }
    foreach ($metric in $metricNames) {
        $coldValues = @($runs | Where-Object kind -eq 'cold-state' | ForEach-Object { $_.$metric })
        $warmValues = @($runs | Where-Object kind -eq 'warm-state' | ForEach-Object { $_.$metric })
        $coldMedian = Get-Median $coldValues
        $warmMedian = Get-Median $warmValues
        $summary.cold[$metric] = if ($coldMedian -eq $null) { $null } else { [math]::Round($coldMedian, 1) }
        $summary.warm[$metric] = if ($warmMedian -eq $null) { $null } else { [math]::Round($warmMedian, 1) }
    }

    $stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
    $resultPath = Join-Path $BuildLogs "startup-performance-$stamp.json"
    $summary | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $resultPath -Encoding UTF8
    Write-Host ('STARTUP_PERF_SUMMARY=' + (($summary | ConvertTo-Json -Depth 8 -Compress)))
    Write-Host "Startup performance report: $resultPath"
}
finally {
    Remove-Item -LiteralPath $fixtureRoot -Recurse -Force -ErrorAction SilentlyContinue
}
