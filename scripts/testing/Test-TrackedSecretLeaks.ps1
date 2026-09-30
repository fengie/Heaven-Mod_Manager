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
$forbiddenExtensions = @('.pfx', '.p12', '.jks', '.keystore', '.kdbx', '.snk')
$binarySkip = @('.zip', '.png', '.jpg', '.jpeg', '.gif', '.webp', '.ico', '.exe', '.dll', '.pdb', '.bin', '.7z')

# Build high-signal signatures from fragments so this scanner cannot match its own source.
$patterns = @(
    [pscustomobject]@{
        Name = 'GitHub access token'
        Pattern = ('gh' + '[pousr]_' + '[A-Za-z0-9_]{20,}')
    },
    [pscustomobject]@{
        Name = 'GitHub fine-grained token'
        Pattern = ('github_' + 'pat_' + '[A-Za-z0-9_]{20,}')
    },
    [pscustomobject]@{
        Name = 'AWS access key id'
        Pattern = ('AKI' + 'A[0-9A-Z]{16}')
    },
    [pscustomobject]@{
        Name = 'Slack token'
        Pattern = ('xox' + '[baprs]-[0-9A-Za-z-]{10,}')
    },
    [pscustomobject]@{
        Name = 'private key material'
        Pattern = ('-----BEGIN ' + '(?:(?:RSA|EC|DSA|OPENSSH) )?PRIVATE KEY-----')
    },
    [pscustomobject]@{
        Name = 'Heaven bridge HMAC key assignment'
        Pattern = ('(?i)HEAVEN_BRIDGE_' + 'HMAC_KEY\s*[:=]\s*[A-Za-z0-9+/_=-]{16,}')
    },
    [pscustomobject]@{
        Name = 'npm auth token'
        Pattern = ('(?i)_auth' + 'Token\s*=\s*(?!\$\{)[^\s#]+')
    }
)

foreach ($relative in $tracked) {
    if ([string]::IsNullOrWhiteSpace($relative)) { continue }
    $full = Join-Path $Root $relative
    if (-not (Test-Path -LiteralPath $full -PathType Leaf)) { continue }

    $leaf = [IO.Path]::GetFileName($relative)
    $extension = [IO.Path]::GetExtension($relative)

    if ($forbiddenExtensions -contains $extension) {
        $errors.Add("$($relative): tracked credential-container/signing-key extension '$extension' is forbidden.")
        continue
    }

    $isEnvironmentFile = $leaf -eq '.env' -or $leaf.StartsWith('.env.', [StringComparison]::OrdinalIgnoreCase)
    $isEnvironmentTemplate = $leaf -match '(?i)\.(example|sample|template)$'
    if ($isEnvironmentFile -and -not $isEnvironmentTemplate) {
        $errors.Add("$($relative): tracked environment file is forbidden; commit a redacted example/template instead.")
        continue
    }

    if ($binarySkip -contains $extension) { continue }

    try {
        $content = Get-Content -LiteralPath $full -Raw -ErrorAction Stop
    }
    catch {
        continue
    }

    foreach ($pattern in $patterns) {
        if ([regex]::IsMatch($content, [string]$pattern.Pattern)) {
            $errors.Add("$($relative): possible $($pattern.Name) detected.")
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
