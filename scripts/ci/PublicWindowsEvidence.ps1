# These project into new values. Never echo raw report text, exception messages,
# paths, profile IDs, last-success strings, or object fields not listed here.
function Get-MhwPublicSha([object]$Value,[int]$Digits = 40) {
    if (($Digits -ne 40 -and $Digits -ne 64) -or $Value -isnot [string] -or
        $Value -cnotmatch ('\A[0-9a-fA-F]{' + $Digits + '}\z')) {
        throw 'Public receipt refused a malformed source or digest.'
    }
    return $Value.ToLowerInvariant()
}

function Get-MhwPublicCount([object]$Value) {
    if (($Value -isnot [int] -and $Value -isnot [long]) -or $Value -lt 0 -or $Value -gt 1000000000) {
        throw 'Public receipt refused a malformed count.'
    }
    return [long]$Value
}

function Get-MhwPublicMeasure([object]$Value,[switch]$Optional) {
    if ($null -eq $Value -and $Optional) { return $null }
    if ($Value -isnot [int] -and $Value -isnot [long] -and
        $Value -isnot [double] -and $Value -isnot [decimal] -and $Value -isnot [single]) {
        throw 'Public receipt refused a nonnumeric measurement.'
    }
    $n = [double]$Value
    if ([double]::IsNaN($n) -or [double]::IsInfinity($n) -or $n -lt 0 -or $n -gt 1000000000) {
        throw 'Public receipt refused an out-of-bounds measurement.'
    }
    return $n
}

function Get-MhwPublicGitIdentity([string]$Root,[object]$ExpectedSource = $null) {
    $source = & git -C $Root rev-parse --verify HEAD 2>$null
    if ($LASTEXITCODE -ne 0) { throw 'Public receipt could not read checked-out source.' }
    $source = Get-MhwPublicSha $source
    if ($null -ne $ExpectedSource -and $source -cne (Get-MhwPublicSha $ExpectedSource)) {
        throw 'Public receipt refused source drift.'
    }
    $tree = & git -C $Root rev-parse --verify 'HEAD^{tree}' 2>$null
    if ($LASTEXITCODE -ne 0) { throw 'Public receipt could not read checked-out tree.' }
    return [ordered]@{ sourceSha=$source; treeSha=(Get-MhwPublicSha $tree) }
}

function Get-MhwPublicWindowsVerification([string]$Raw) {
    $summary = [regex]::Matches($Raw,'(?m)^Overall: \*\*(PASS|FAIL)\*\* - ([0-9]{1,9}) passed / ([0-9]{1,9}) failed\r?$')
    if ($summary.Count -ne 1) { throw 'Public receipt refused an invalid verification summary.' }
    $passed = [int]$summary[0].Groups[2].Value
    $failed = [int]$summary[0].Groups[3].Value
    $claimed = $summary[0].Groups[1].Value
    if (($claimed -ceq 'PASS' -and ($passed -le 0 -or $failed -ne 0)) -or
        ($claimed -ceq 'FAIL' -and $failed -le 0)) {
        throw 'Public receipt refused inconsistent verification counts.'
    }
    return [ordered]@{ status=$claimed; passed=$passed; failed=$failed }
}

function Get-MhwPublicReleasePackage($Manifest,[string]$ExpectedSource,[string]$ExpectedVersion) {
    if ($ExpectedVersion -cnotmatch '\A[0-9]{1,5}\.[0-9]{1,5}\.[0-9]{1,5}\z' -or
        $Manifest.productVersion -isnot [string] -or $Manifest.productVersion -cne $ExpectedVersion -or
        (Get-MhwPublicSha $Manifest.sourceSha) -cne (Get-MhwPublicSha $ExpectedSource)) {
        throw 'Public receipt refused package identity drift.'
    }
    $build = $Manifest.buildNumber
    if (($build -isnot [int] -and $build -isnot [long]) -or $build -le 0) {
        throw 'Public receipt refused a malformed build number.'
    }
    return [ordered]@{
        version=$ExpectedVersion
        buildNumber=$build
        zipSha256=(Get-MhwPublicSha $Manifest.sha256 64)
        contentsManifestSha256=(Get-MhwPublicSha $Manifest.productManifestSha256 64)
    }
}

function Get-MhwPublicWpfCounts($Summary) {
    $counts = [ordered]@{}
    foreach ($name in @('expectedCount','passedCount','failedCount','notRunCount','invalidCount','unexpectedCount')) {
        $counts[$name] = Get-MhwPublicCount $Summary.$name
    }
    if ($counts.expectedCount -le 0 -or
        ($counts.passedCount + $counts.failedCount + $counts.notRunCount + $counts.invalidCount) -ne $counts.expectedCount) {
        throw 'Public receipt refused inconsistent WPF fixture counts.'
    }
    if ($Summary.complete -isnot [bool] -or
        $Summary.complete -ne ($counts.passedCount -eq $counts.expectedCount -and $counts.unexpectedCount -eq 0)) {
        throw 'Public receipt refused inconsistent WPF completion.'
    }
    $counts['complete'] = $Summary.complete
    return $counts
}

function Get-MhwPublicStartupMeasurements($Summary,[string]$ExpectedSource) {
    $source = Get-MhwPublicSha $Summary.source_sha
    if ($source -cne (Get-MhwPublicSha $ExpectedSource)) { throw 'Public receipt refused stale performance data.' }
    $result = [ordered]@{ source_sha=$source }
    foreach ($key in @('mod_count','files_per_mod','inbox_packages','files_per_inbox_package','total_mod_files')) {
        $result[$key] = Get-MhwPublicCount $Summary.$key
    }
    if ($result.mod_count -le 0 -or $result.files_per_mod -le 0 -or
        $result.total_mod_files -ne ($result.mod_count * $result.files_per_mod)) {
        throw 'Public receipt refused inconsistent performance fixture counts.'
    }
    $result['package_size_mb'] = Get-MhwPublicMeasure $Summary.package_size_mb
    foreach ($phase in @('cold','warm')) {
        $result[$phase] = [ordered]@{}
        foreach ($metric in @('process_to_ready_ms','session_to_main_window_show_ms','session_to_ready_ms','cpu_ms',
            'peak_working_set_mb','idle_working_set_mb','catalog_refresh_ms','armor_import_ms','nexus_refresh_ms',
            'game_build_ms','recovery_ms','automation_ms','main_window_initialize_ms','reload_mods_ms',
            'refresh_analysis_ms','master_log_mb')) {
            $required = $metric -in @('process_to_ready_ms','peak_working_set_mb')
            $result[$phase][$metric] = Get-MhwPublicMeasure $Summary.$phase.$metric -Optional:(!$required)
        }
    }
    return $result
}

function Write-MhwPublicSummary(
    [ValidateSet('feature-pr','updater-publication-pr','windows-release','packaged-wpf','updater-e2e')][string]$Kind,
    [Parameter(Mandatory=$true)]$Identity,
    [Parameter(Mandatory=$true)][ValidateSet('success','failure','skipped','cancelled')][string]$Outcome,
    $Proof
) {
    if ($env:GITHUB_RUN_ID -cnotmatch '\A[0-9]{1,18}\z' -or $env:GITHUB_RUN_ATTEMPT -cnotmatch '\A[0-9]{1,8}\z') {
        throw 'Public receipt refused malformed run identity.'
    }
    # The final publication sink owns the allowlist. A caller passing the
    # entire raw document, exception, or a future field cannot publish it.
    $source = Get-MhwPublicSha $Identity.sourceSha
    $safeProof = $null
    switch ($Kind) {
        'packaged-wpf' {
            if ($null -ne $Proof) { $safeProof = Get-MhwPublicWpfCounts $Proof }
            if ($Outcome -ceq 'success') {
                if ($null -eq $safeProof -or -not $safeProof.complete -or
                    $env:RUNNER_ENVIRONMENT -cne 'github-hosted' -or $env:RUNNER_OS -cne 'Windows') {
                    throw 'Public receipt refused incomplete or non-disposable WPF evidence.'
                }
                $safeProof['exeSha256'] = Get-MhwPublicSha $Proof.exeSha256 64
                $safeProof['hostedDisposable'] = $true
            }
        }
        'windows-release' {
            if ($Outcome -ceq 'success') {
                $summary = Get-MhwPublicWindowsVerification $Proof.verificationText
                if ($summary.status -cne 'PASS') { throw 'Public receipt refused incomplete release verification.' }
                $safeProof = [ordered]@{
                    verification=$summary
                    package=(Get-MhwPublicReleasePackage $Proof.manifest $source $Proof.version)
                }
            }
        }
        'updater-e2e' {
            if ($Outcome -ceq 'success') {
                if ($env:RUNNER_ENVIRONMENT -cne 'github-hosted' -or $env:RUNNER_OS -cne 'Windows') {
                    throw 'Public receipt refused non-disposable updater evidence.'
                }
                try {
                    . (Join-Path $PSScriptRoot '..\release\UpdaterReleasePolicy.ps1')
                    $publicJson = ConvertTo-UpdaterInstalledClientDurableEvidenceJson -Json $Proof.raw -ExpectedTargetSourceSha $source
                    $safeProof = $publicJson | ConvertFrom-Json -ErrorAction Stop
                    foreach ($fixture in @('mod','downloads','state','unknownUserFile')) {
                        $updated = Get-MhwPublicSha $safeProof.update.sentinelSha256.$fixture 64
                        $restored = Get-MhwPublicSha $safeProof.rollback.sentinelSha256.$fixture 64
                        if ($updated -cne $restored) { throw 'Fixture digest changed.' }
                    }
                } catch {
                    throw 'Public receipt refused invalid installed-client proof.'
                }
            }
        }
    }
    $receipt = [ordered]@{
        schema='mhw-mod-manager/public-windows-evidence/v1'
        kind=$Kind
        sourceSha=$source
        treeSha=(Get-MhwPublicSha $Identity.treeSha)
        runId=$env:GITHUB_RUN_ID
        runAttempt=[int]$env:GITHUB_RUN_ATTEMPT
        outcome=$Outcome
    }
    if ($null -ne $safeProof) { $receipt['proof'] = $safeProof }
    $summary = @('### MHW public verification receipt', '', ('~~~json'), ($receipt | ConvertTo-Json -Depth 12), '~~~')
    $summary | Add-Content -LiteralPath $env:GITHUB_STEP_SUMMARY -Encoding UTF8
}
