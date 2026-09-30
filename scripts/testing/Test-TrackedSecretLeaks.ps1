param(
    [string]$Root = ''
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if ([string]::IsNullOrWhiteSpace($Root)) {
    $Root = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
}
else {
    $Root = (Resolve-Path -LiteralPath $Root).Path
}

$git = (Get-Command git -ErrorAction Stop).Source
$tracked = @(& $git -C $Root ls-files)
if ($LASTEXITCODE -ne 0) {
    throw 'Unable to enumerate tracked files for secret-leak policy.'
}

$errors = New-Object System.Collections.Generic.List[string]
$forbiddenExtensions = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($ext in @('.pfx', '.p12', '.jks', '.keystore', '.kdbx')) {
    [void]$forbiddenExtensions.Add($ext)
}
$binarySkip = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($ext in @('.zip', '.png', '.jpg', '.jpeg', '.gif', '.webp', '.ico', '.exe', '.dll', '.pdb', '.bin', '.7z')) {
    [void]$binarySkip.Add($ext)
}

# Build high-signal credential patterns from fragments so the scanner source does not match itself.
$patterns = @(
    [pscustomobject]@{
        Name = 'GitHub access token'
        Regex = [regex]::new(('gh' + '[pousr]_' + '[A-Za-z0-9_]{20,}'), [Text.RegularExpressions.RegexOptions]::CultureInvariant)
    },
    [pscustomobject]@{
        Name = 'AWS access key id'
        Regex = [regex]::new(('AKI' + 'A[0-9A-Z]{16}'), [Text.RegularExpressions.RegexOptions]::CultureInvariant)
    },
    [pscustomobject]@{
        Name = 'Slack token'
        Regex = [regex]::new(('xox' + '[baprs]-[0-9A-Za-z-]{10,}'), [Text.RegularExpressions.RegexOptions]::CultureInvariant)
    },
    [pscustomobject]@{
        Name = 'private key material'
        Regex = [regex]::new(('-----BEGIN ' + '(?:(?:RSA|EC|DSA|OPENSSH) )?PRIVATE KEY-----'), [Text.RegularExpressions.RegexOptions]::CultureInvariant)
    },
    [pscustomobject]@{
        Name = 'Heaven bridge HMAC key assignment'
        Regex = [regex]::new(('HEAVEN_BRIDGE_' + 'HMAC_KEY\s*[:=]\s*["'']?[A-Za-z0-9+/_=-]{16,}'), [Text.RegularExpressions.RegexOptions]::IgnoreCase)
    },
    [pscustomobject]@{
        Name = 'npm auth token'
        Regex = [regex]::new(('_auth' + 'Token\s*=\s*(?!\$\{)[^\s#]+'), [Text.RegularExpressions.RegexOptions]::IgnoreCase)
    }
)

foreach ($relative in $tracked) {
    if ([string]::IsNullOrWhiteSpace($relative)) { continue }
    $full = Join-Path $Root $relative
    if (-not (Test-Path -LiteralPath $full -PathType Leaf)) { continue }

    $leaf = [IO.Path]::GetFileName($relative)
    $extension = [IO.Path]::GetExtension($relative)

    if ($forbiddenExtensions.Contains($extension)) {
        $errors.Add("$relative: tracked credential-container extension '$extension' is forbidden.")
        continue
    }

    if ($leaf -eq '.env' -or ($leaf.StartsWith('.env.', [StringComparison]::OrdinalIgnoreCase)
        -and $leaf -notmatch '(?i)\.(example|sample|template)$')) {
        $errors.Add("$relative: tracked environment file is forbidden; commit a redacted example/template instead.")
        continue
    }

    if ($binarySkip.Contains($extension)) { continue }

    try {
        $content = Get-Content -LiteralPath $full -Raw -ErrorAction Stop
    }
    catch {
        continue
    }

    foreach ($pattern in $patterns) {
        if ($pattern.Regex.IsMatch($content)) {
            $errors.Add("$relative: possible $($pattern.Name) detected.")
        }
    }
}

if ($errors.Count -gt 0) {
    Write-Host "Tracked-secret policy failed with $($errors.Count) finding(s):" -ForegroundColor Red
    foreach ($item in $errors) {
        Write-Host " - $item" -ForegroundColor Red
    }
    throw 'Tracked files contain material that resembles credentials or private key data.'
}

Write-Host "PASS: tracked-secret policy ($($tracked.Count) tracked paths checked)." -ForegroundColor Green
