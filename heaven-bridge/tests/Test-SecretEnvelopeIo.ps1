$ErrorActionPreference = 'Stop'
$lib = Join-Path (Split-Path -Parent $PSScriptRoot) 'secret-envelope-io.ps1'
. $lib

$root = Join-Path ([IO.Path]::GetTempPath()) ('heaven-secret-io-test-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($root) | Out-Null
$canary = 'HEAVEN-SECRET-PUBLISH-FAILURE-CANARY-2a67f1'
$handle = '0123456789abcdef0123456789abcdef0123456789abcdef'
$json = ('{"schema":"test","value":"' + $canary + '"}')
try {
  $failed = $false
  try {
    Publish-HeavenSecretEnvelopeFile -ResolvedInbox $root -Handle $handle -Json $json -MoveAction {
      param($source, $destination)
      throw 'forced publish rename failure'
    } | Out-Null
  } catch {
    $failed = $true
    if ($_.Exception.Message -notlike '*forced publish rename failure*') { throw }
  }
  if (-not $failed) { throw 'Expected forced publish failure was not observed.' }

  $files = @(Get-ChildItem -LiteralPath $root -File -Force -ErrorAction SilentlyContinue)
  if ($files.Count -ne 0) {
    foreach ($file in $files) {
      $text = Get-Content -LiteralPath $file.FullName -Raw -ErrorAction SilentlyContinue
      if ($text -and $text.Contains($canary)) {
        throw "Secret-bearing temp file survived failed publication: $($file.Name)"
      }
    }
    throw "Failed publication left unexpected files: $($files.Name -join ', ')"
  }

  Write-Output 'HEAVEN_SECRET_ENVELOPE_IO_TEST_OK'
} finally {
  Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
  $canary = $null
  $json = $null
}
