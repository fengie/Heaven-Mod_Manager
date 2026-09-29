param(
    [string]$InboxPath = $env:HEAVEN_BRIDGE_SECRET_INBOX,
    [string]$Destination = "heaven",
    [Parameter(Mandatory = $true)]
    [long]$TargetHwnd,
    [ValidateRange(10, 300)]
    [int]$TtlSeconds = 120,
    [string]$Handle
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($InboxPath)) {
    throw "HEAVEN_BRIDGE_SECRET_INBOX or -InboxPath is required."
}
if (-not (Test-Path -LiteralPath $InboxPath -PathType Container)) {
    throw "Secret inbox is unavailable."
}
if ($TargetHwnd -le 0) {
    throw "TargetHwnd must be a positive HWND."
}
if ([string]::IsNullOrWhiteSpace($Handle)) {
    $Handle = [guid]::NewGuid().ToString("N")
}
if ($Handle -notmatch '^[A-Za-z0-9._-]{1,120}$') {
    throw "Handle contains unsupported characters."
}

$secure = Read-Host -Prompt "Secret" -AsSecureString
$bstr = [IntPtr]::Zero
$plain = $null
$tmp = $null
try {
    $bstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
    $plain = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr)
    if ($null -eq $plain -or $plain.Length -gt 4096) {
        throw "Secret is empty or exceeds the supported size."
    }

    $created = [DateTimeOffset]::UtcNow
    $expires = $created.AddSeconds($TtlSeconds)
    $envelope = [ordered]@{
        schema      = "heaven-bridge-secret-v1"
        handle      = $Handle
        destination = $Destination
        purpose     = "secret_type"
        target_hwnd = $TargetHwnd
        created_at  = $created.ToString("o")
        expires_at  = $expires.ToString("o")
        value       = $plain
    }

    $final = Join-Path $InboxPath ($Handle + ".json")
    $tmp = Join-Path $InboxPath ("." + $Handle + "." + [guid]::NewGuid().ToString("N") + ".tmp")
    $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
    [IO.File]::WriteAllText($tmp, ($envelope | ConvertTo-Json -Compress), $utf8NoBom)
    Move-Item -LiteralPath $tmp -Destination $final -Force
    $tmp = $null

    [pscustomobject]@{
        handle      = $Handle
        target_hwnd = $TargetHwnd
        destination = $Destination
        expires_at  = $expires.ToString("o")
    } | ConvertTo-Json -Compress
}
finally {
    $plain = $null
    if ($bstr -ne [IntPtr]::Zero) {
        [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr)
    }
    if ($tmp -and (Test-Path -LiteralPath $tmp)) {
        Remove-Item -LiteralPath $tmp -Force -ErrorAction SilentlyContinue
    }
}
