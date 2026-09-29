function Publish-HeavenSecretEnvelopeFile {
  [CmdletBinding()]
  param(
    [Parameter(Mandatory=$true)][string]$ResolvedInbox,
    [Parameter(Mandatory=$true)][string]$Handle,
    [Parameter(Mandatory=$true)][string]$Json,
    [scriptblock]$MoveAction
  )

  $temp = $null
  $final = Join-Path $ResolvedInbox ("$Handle.json")
  try {
    $temp = Join-Path $ResolvedInbox (".$Handle.tmp-$PID-$([Guid]::NewGuid().ToString('N')).json")
    [IO.File]::WriteAllText($temp, $Json, [Text.UTF8Encoding]::new($false))
    if ($MoveAction) {
      & $MoveAction $temp $final
    } else {
      Move-Item -LiteralPath $temp -Destination $final -ErrorAction Stop
    }
    return $final
  } finally {
    if ($temp -and (Test-Path -LiteralPath $temp)) {
      Remove-Item -LiteralPath $temp -Force -ErrorAction SilentlyContinue
    }
  }
}
