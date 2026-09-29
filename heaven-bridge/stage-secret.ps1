[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$TargetJson,

    [string]$Destination = "heaven",

    [ValidateRange(15, 120)]
    [int]$TtlSeconds = 120
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$inbox = [Environment]::GetEnvironmentVariable("HEAVEN_BRIDGE_SECRET_AUTHORITY_INBOX")
if ([string]::IsNullOrWhiteSpace($inbox)) {
    throw "HEAVEN_BRIDGE_SECRET_AUTHORITY_INBOX is not configured on the credential-authority machine."
}
if (-not (Test-Path -LiteralPath $inbox -PathType Container)) {
    throw "The configured credential-authority inbox is unavailable."
}

try {
    $target = $TargetJson | ConvertFrom-Json -ErrorAction Stop
}
catch {
    throw "TargetJson must be a valid JSON object."
}
if ($null -eq $target -or $target -is [string] -or $target -is [System.Array]) {
    throw "TargetJson must be a JSON object."
}

$allowedTargetFields = @("hwnd", "pid", "title", "visible_only", "first_match")
$targetFields = @($target.PSObject.Properties.Name)
$unknown = @($targetFields | Where-Object { $_ -notin $allowedTargetFields })
if ($unknown.Count -gt 0) {
    throw "TargetJson contains unsupported fields."
}
if (($null -eq $target.hwnd) -and ($null -eq $target.pid) -and [string]::IsNullOrWhiteSpace([string]$target.title)) {
    throw "TargetJson must bind the secret to hwnd, pid, or title."
}

$secure = Read-Host "Secret value (input is hidden)" -AsSecureString
if ($secure.Length -lt 1 -or $secure.Length -gt 10000) {
    throw "Secret value must contain 1-10000 characters."
}

$rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
$random = New-Object byte[] 24
try {
    $rng.GetBytes($random)
}
finally {
    $rng.Dispose()
}
$handle = [Convert]::ToBase64String($random).TrimEnd("=").Replace("+", "-").Replace("/", "_")
[Array]::Clear($random, 0, $random.Length)

$created = [DateTimeOffset]::UtcNow
$expires = $created.AddSeconds($TtlSeconds)
$bstr = [IntPtr]::Zero
$plain = $null
$tempPath = $null
try {
    $bstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
    $plain = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr)

    $envelope = [ordered]@{
        version = 1
        destination = $Destination.Trim().ToLowerInvariant()
        purpose = "secret_type"
        created_at = $created.ToString("o")
        expires_at = $expires.ToString("o")
        target = $target
        secret = $plain
    }

    $json = $envelope | ConvertTo-Json -Compress -Depth 8
    $targetPath = Join-Path $inbox ($handle + ".json")
    $tempPath = Join-Path $inbox ("." + $handle + ".tmp-" + [Guid]::NewGuid().ToString("N"))
    [IO.File]::WriteAllText($tempPath, $json, (New-Object Text.UTF8Encoding($false)))
    [IO.File]::Move($tempPath, $targetPath)
    $tempPath = $null
}
finally {
    if ($bstr -ne [IntPtr]::Zero) {
        [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr)
    }
    $plain = $null
    $json = $null
    $secure = $null
    if ($tempPath -and (Test-Path -LiteralPath $tempPath)) {
        Remove-Item -LiteralPath $tempPath -Force -ErrorAction SilentlyContinue
    }
}

[pscustomobject]@{
    handle = $handle
    destination = $Destination.Trim().ToLowerInvariant()
    expires_at = $expires.ToString("o")
    target = $target
} | ConvertTo-Json -Compress -Depth 8
