$ErrorActionPreference = 'Stop'
$validator = Join-Path $PSScriptRoot 'Verify-HeavenRepoPolicy.ps1'
$policyPath = Join-Path $PSScriptRoot '../../.heaven/update-policy.json'
$source = Get-Content -LiteralPath $policyPath -Raw -Encoding UTF8
$policy = $source | ConvertFrom-Json
$tempRoot = (Get-Item -LiteralPath ([IO.Path]::GetTempPath())).FullName.TrimEnd('\', '/')
$fixture = Join-Path $tempRoot ('mhw-policy-scalars-' + [Guid]::NewGuid().ToString('N'))
$encoding = [Text.UTF8Encoding]::new($false)
$priorRepository = $env:EXPECTED_REPOSITORY

try {
  [void][IO.Directory]::CreateDirectory((Join-Path $fixture '.heaven'))
  foreach ($relative in $policy.runtime_update.evidence_paths) {
    if ([IO.Path]::IsPathRooted($relative) -or $relative -match '(^|[\\/])\.\.([\\/]|$)') {
      throw "Invalid evidence path in fixture source: $relative"
    }
    $path = Join-Path $fixture $relative
    [void][IO.Directory]::CreateDirectory((Split-Path -Parent $path))
    [IO.File]::WriteAllText($path, '', $encoding)
  }
  $env:EXPECTED_REPOSITORY = $policy.repository
  Push-Location -LiteralPath $fixture
  try {
    $fields = @(
      @{
        name = 'required'
        invalid = @('missing', 'null', 'false', '"false"', '"true"', '0', '1', '{}', '[]', '[true]')
        valid = @('true')
        error = 'Automatic main source consumption must be required.'
      },
      @{
        name = 'poll_interval_minutes'
        invalid = @('missing', 'null', '"1"', 'true', '1.0', '1.5', '{}', '[]', '[1]')
        valid = @('1', '60')
        error = 'Source update poll interval must be a JSON integer.'
      },
      @{
        name = 'poll_interval_minutes'
        invalid = @('0', '61')
        valid = @()
        error = 'Source update poll interval must be 1-60 minutes.'
      }
    )
    foreach ($field in $fields) {
      foreach ($literal in ($field.invalid + $field.valid)) {
        $candidate = $source | ConvertFrom-Json
        if ($literal -eq 'missing') {
          $candidate.source_update.PSObject.Properties.Remove($field.name)
          $json = $candidate | ConvertTo-Json -Depth 10
        } else {
          # Insert the raw literal so 1.0 and 1 remain distinct JSON values.
          $candidate.source_update.($field.name) = '__policy_scalar_fixture__'
          $json = ($candidate | ConvertTo-Json -Depth 10).Replace('"__policy_scalar_fixture__"', $literal)
        }
        [IO.File]::WriteAllText((Join-Path $fixture '.heaven/update-policy.json'), $json, $encoding)
        $failure = $null
        $output = $null
        try { $output = & $validator } catch { $failure = $_.Exception.Message }
        if ($literal -in $field.valid) {
          if ($failure -or $output -cne 'HEAVEN_AUTOUPDATE_POLICY_OK') {
            throw "$($field.name)=$literal should pass. Observed '$failure'"
          }
        } elseif ($failure -cne $field.error) {
          throw "$($field.name)=$literal should fail with '$($field.error)'. Observed '$failure'"
        }
      }
    }
    'HEAVEN_AUTOUPDATE_SCALARS_OK'
  } finally { Pop-Location }
} finally {
  $env:EXPECTED_REPOSITORY = $priorRepository
  $owned = Get-Item -LiteralPath $fixture -Force -ErrorAction SilentlyContinue
  if ($owned -and $owned.Parent.FullName.TrimEnd('\', '/') -eq $tempRoot -and
      $owned.Name -match '^mhw-policy-scalars-[0-9a-f]{32}$' -and
      -not ($owned.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
    Remove-Item -LiteralPath $owned.FullName -Recurse -Force
  }
}
