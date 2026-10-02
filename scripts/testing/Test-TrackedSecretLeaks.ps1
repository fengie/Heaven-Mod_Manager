param([string]$Root = '')
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest

if([string]::IsNullOrWhiteSpace($Root)){
    $Root=(Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
}else{
    $Root=(Resolve-Path -LiteralPath $Root).Path
}

$git=(Get-Command git -ErrorAction Stop).Source
$tracked=@(& $git -C $Root ls-files)
if($LASTEXITCODE -ne 0){throw 'Unable to enumerate tracked files for secret scanning.'}

$errors=New-Object System.Collections.Generic.List[string]
$textExtensions=@('.ps1','.psm1','.cs','.json','.yml','.yaml','.md','.txt','.props','.targets','.csproj','.xml','.config','.sh','.bash','.js','.mjs','.cjs','.ts','.tsx','.py','.toml','.ini','.cmd','.bat','.sql')
$sensitivePathPatterns=@(
    '(?i)(^|/)\.env($|\.)',
    '(?i)\.(pfx|p12|snk)$',
    '(?i)(^|/)(id_rsa|id_ed25519)$',
    '(?i)(^|/)(credentials|service-account)(\.[^/]+)?\.json$'
)
$secretPatterns=@(
    @{ Name='private key material'; Regex='-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----' },
    @{ Name='GitHub fine-grained token'; Regex='github_pat_[A-Za-z0-9_]{40,}' },
    @{ Name='GitHub classic token'; Regex='gh[pousr]_[A-Za-z0-9]{30,}' },
    @{ Name='OpenAI-style secret key'; Regex='(?<![A-Za-z0-9])sk-(?:proj-[A-Za-z0-9_-]{40,}|[A-Za-z0-9]{40,})' },
    @{ Name='AWS access key id'; Regex='AKIA[0-9A-Z]{16}' }
)

foreach($relativeRaw in $tracked){
    $relative=([string]$relativeRaw).Replace('\','/')
    if([string]::IsNullOrWhiteSpace($relative)){continue}
    $isTemplate=$relative -match '(?i)(^|/)\.env\.(example|sample|template)$'
    foreach($pattern in $sensitivePathPatterns){
        if(-not $isTemplate -and $relative -match $pattern){
            $errors.Add("Tracked sensitive credential/signing path is forbidden: $relative")
            break
        }
    }

    $extension=[IO.Path]::GetExtension($relative).ToLowerInvariant()
    if($textExtensions -notcontains $extension){continue}
    $full=Join-Path $Root ($relative.Replace('/',[IO.Path]::DirectorySeparatorChar))
    if(!(Test-Path -LiteralPath $full -PathType Leaf)){continue}
    $item=Get-Item -LiteralPath $full
    if($item.Length -gt 2MB){continue}
    $content=Get-Content -LiteralPath $full -Raw
    foreach($pattern in $secretPatterns){
        $matches=[regex]::Matches($content,[string]$pattern.Regex)
        foreach($match in $matches){
            $lineStart=$content.LastIndexOf("`n",[Math]::Max(0,$match.Index-1)) + 1
            $lineEnd=$content.IndexOf("`n",$match.Index)
            if($lineEnd -lt 0){$lineEnd=$content.Length}
            $line=$content.Substring($lineStart,$lineEnd-$lineStart)
            if($line -match '(?i)(example|placeholder|dummy|fake|redacted|test[_ -]?only)'){continue}
            $errors.Add("Tracked $($pattern.Name) detected in $relative; remove/rotate the secret and purge it from tracked source.")
            break
        }
    }
}

if($errors.Count -gt 0){
    Write-Host "Tracked secret/private-key scan failed with $($errors.Count) violation(s):" -ForegroundColor Red
    foreach($item in $errors){Write-Host " - $item" -ForegroundColor Red}
    throw 'Tracked credential/private-key material is forbidden.'
}
Write-Host 'PASS: tracked source contains no high-confidence credential/private-key material.' -ForegroundColor Green
