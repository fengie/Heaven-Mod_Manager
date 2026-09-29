param(
  [Parameter(Mandatory=$true)][string]$InboxPath,
  [Parameter(Mandatory=$true)][long]$Hwnd,
  [string]$Destination = 'heaven',
  [ValidateRange(30,300)][int]$TtlSeconds = 120
)

$ErrorActionPreference = 'Stop'
if ($Hwnd -le 0) { throw 'Hwnd must be a positive top-level window handle.' }
if ([string]::IsNullOrWhiteSpace($Destination)) { throw 'Destination is required.' }
if (-not $InboxPath.StartsWith('\\')) {
  throw 'InboxPath must be an SMB UNC path. Do not stage credential envelopes in the repository or a local relay folder.'
}

# Touch the share first so Windows establishes the SMB session, then verify transport encryption.
$resolvedInbox = (Get-Item -LiteralPath $InboxPath -ErrorAction Stop).FullName
$parts = $resolvedInbox.TrimStart('\').Split('\')
if ($parts.Length -lt 2) { throw 'InboxPath must identify an SMB server and share.' }
$server = $parts[0]
$share = $parts[1]
$connection = Get-SmbConnection -ServerName $server -ErrorAction Stop |
  Where-Object { $_.ShareName -eq $share } |
  Select-Object -First 1
if (-not $connection) { throw "No SMB connection was found for \\$server\$share." }
if (-not $connection.Encrypted) {
  throw "SMB encryption is not active for \\$server\$share. Refusing to stage a credential."
}

$secret = Read-Host 'Secret value' -AsSecureString
$rng = [Security.Cryptography.RandomNumberGenerator]::Create()
$bytes = New-Object byte[] 24
$rng.GetBytes($bytes)
$rng.Dispose()
$handle = ([BitConverter]::ToString($bytes)).Replace('-', '').ToLowerInvariant()

$destinationNormalized = $Destination.Trim().ToLowerInvariant()
$bindingMaterial = "gui_type_secret|$destinationNormalized|hwnd:$Hwnd"
$sha = [Security.Cryptography.SHA256]::Create()
try {
  $bindingBytes = [Text.Encoding]::UTF8.GetBytes($bindingMaterial)
  $targetBinding = ([BitConverter]::ToString($sha.ComputeHash($bindingBytes))).Replace('-', '').ToLowerInvariant()
} finally {
  $sha.Dispose()
}

$created = [DateTimeOffset]::UtcNow
$expires = $created.AddSeconds($TtlSeconds)
$bstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secret)
$plain = $null
try {
  $plain = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr)
  $envelope = [ordered]@{
    schema = 'heaven-secret-envelope-v1'
    handle = $handle
    destination = $destinationNormalized
    purpose = 'gui_type_secret'
    created_at = $created.ToString('o')
    expires_at = $expires.ToString('o')
    target_binding = $targetBinding
    value = $plain
  }
  $json = $envelope | ConvertTo-Json -Compress
  $temp = Join-Path $resolvedInbox (".$handle.tmp-$PID-$([Guid]::NewGuid().ToString('N')).json")
  $final = Join-Path $resolvedInbox ("$handle.json")
  [IO.File]::WriteAllText($temp, $json, [Text.UTF8Encoding]::new($false))
  Move-Item -LiteralPath $temp -Destination $final -ErrorAction Stop
} finally {
  if ($bstr -ne [IntPtr]::Zero) { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr) }
  $plain = $null
  $json = $null
}

# Output only non-secret relay metadata.
[ordered]@{
  handle = $handle
  hwnd = $Hwnd
  destination = $destinationNormalized
  expires_at = $expires.ToString('o')
} | ConvertTo-Json -Compress
