param([switch]$VerifierOnly)
$ErrorActionPreference='Stop'
$Root=(Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
. (Join-Path $PSScriptRoot '..\diagnostics\Master-Debug.ps1')
$Area='PS-SYNTAX'
Write-MhwMasterDebug -Root $Root -Area $Area -Message ("Syntax preflight begin. VerifierOnly={0}" -f $VerifierOnly)
if($VerifierOnly){
    $Targets=@(Get-Item (Join-Path $PSScriptRoot '..\release\Verify-Release.ps1'))
}else{
    $ScriptsRoot=(Resolve-Path (Join-Path $PSScriptRoot '..')).Path
    $Targets=@(Get-ChildItem $ScriptsRoot -Filter '*.ps1' -File -Recurse | Sort-Object FullName)
}
$ErrorsFound=New-Object System.Collections.Generic.List[string]
foreach($Target in $Targets){
    Write-MhwMasterDebug -Root $Root -Area $Area -Message ("Parsing: {0}" -f $Target.FullName)
    $Tokens=$null
    $ParseErrors=$null
    [System.Management.Automation.Language.Parser]::ParseFile($Target.FullName,[ref]$Tokens,[ref]$ParseErrors) | Out-Null
    if($ParseErrors -and $ParseErrors.Count -gt 0){
        foreach($ParseError in $ParseErrors){
            $ErrorsFound.Add(("{0}:{1}:{2}: {3}" -f $Target.FullName,$ParseError.Extent.StartLineNumber,$ParseError.Extent.StartColumnNumber,$ParseError.Message))
        }
    }
}
if($ErrorsFound.Count -gt 0){
    Write-Host "PowerShell parser found $($ErrorsFound.Count) error(s):" -ForegroundColor Red
    foreach($Line in $ErrorsFound){Write-Host $Line -ForegroundColor Red;Write-MhwMasterDebug -Root $Root -Area $Area -Message ("FAIL: "+$Line)}
    Write-MhwMasterDebug -Root $Root -Area $Area -Message ("Syntax preflight FAILED with {0} parse error(s)." -f $ErrorsFound.Count)
    exit 3
}
Write-Host "PowerShell syntax preflight passed for $($Targets.Count) script(s)." -ForegroundColor Green
Write-MhwMasterDebug -Root $Root -Area $Area -Message ("Syntax preflight PASS for {0} script(s)." -f $Targets.Count)
exit 0
