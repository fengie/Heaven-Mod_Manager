$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'UpdaterReleasePolicy.ps1')

function Assert-Equal {
  param($Expected,$Actual,[string]$Label)
  if($Expected -ne $Actual){throw "$Label expected '$Expected' but got '$Actual'."}
}

Assert-Equal 42 (Get-UpdaterBuildFromTag 'updater-main-42') 'tag build'
Assert-Equal -1 (Get-UpdaterBuildFromTag 'v8.8.0') 'foreign tag'
Assert-Equal $true (Test-UpdaterReleaseRelevantPath 'src/MhwModManager.App/App.xaml.cs') 'src relevant'
Assert-Equal $true (Test-UpdaterReleaseRelevantPath 'scripts/Build-Release.ps1') 'scripts relevant'
Assert-Equal $true (Test-UpdaterReleaseRelevantPath 'VERSION.txt') 'version relevant'
Assert-Equal $false (Test-UpdaterReleaseRelevantPath '_AGENT_CONTEXT/EVIDENCE/x.log') 'evidence irrelevant'
Assert-Equal $false (Test-UpdaterReleaseRelevantPath '.verification/function-status.json') 'cache irrelevant'

$current='aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa'
$previous='bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb'

$stale=Get-UpdaterPublicationDecision -CurrentBuild 20 -CurrentSourceSha $current -RemoteMainSha $previous
Assert-Equal $false $stale.Publish 'stale main publish'
Assert-Equal 'stale-main' $stale.Reason 'stale main reason'

$evidence=Get-UpdaterPublicationDecision -CurrentBuild 20 -CurrentSourceSha $current -RemoteMainSha $current -PreviousBuild 19 -PreviousSourceSha $previous -ChangedPaths @('_AGENT_CONTEXT/EVIDENCE/x.log','.verification/function-status.json')
Assert-Equal $false $evidence.Publish 'evidence-only publish'
Assert-Equal 'no-release-input-change' $evidence.Reason 'evidence-only reason'

$product=Get-UpdaterPublicationDecision -CurrentBuild 20 -CurrentSourceSha $current -RemoteMainSha $current -PreviousBuild 19 -PreviousSourceSha $previous -ChangedPaths @('_AGENT_CONTEXT/CURRENT_STATE.md','src/MhwModManager.Updater/UpdateRuntime.cs')
Assert-Equal $true $product.Publish 'product publish'
Assert-Equal 'release-input-change' $product.Reason 'product reason'

$already=Get-UpdaterPublicationDecision -CurrentBuild 20 -CurrentSourceSha $current -RemoteMainSha $current -PreviousBuild 20 -PreviousSourceSha $current -ChangedPaths @('src/x.cs')
Assert-Equal $false $already.Publish 'same build publish'
Assert-Equal 'already-published-build' $already.Reason 'same build reason'

$threw=$false
try{
  [void](Get-UpdaterPublicationDecision -CurrentBuild 18 -CurrentSourceSha $current -RemoteMainSha $current -PreviousBuild 19 -PreviousSourceSha $previous -ChangedPaths @('src/x.cs'))
}catch{$threw=$true}
Assert-Equal $true $threw 'older build rejection'

Write-Host 'PASS: updater release publication policy' -ForegroundColor Green
