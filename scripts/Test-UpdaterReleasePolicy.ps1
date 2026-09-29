$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'UpdaterReleasePolicy.ps1')
. (Join-Path $PSScriptRoot 'UpdaterReleasePublication.ps1')

function Assert-Equal {
  param($Expected,$Actual,[string]$Label)
  if($Expected -ne $Actual){throw "$Label expected '$Expected' but got '$Actual'."}
}

Assert-Equal 42 (Get-UpdaterBuildFromTag 'updater-main-42') 'tag build'
Assert-Equal -1 (Get-UpdaterBuildFromTag 'v8.8.0') 'foreign tag'
Assert-Equal $true (Test-UpdaterReleaseRelevantPath 'src/MhwModManager.App/App.xaml.cs') 'src relevant'
Assert-Equal $true (Test-UpdaterReleaseRelevantPath 'tests/MhwModManager.IntegrationTests/UpdaterInstalledClientE2ETests.cs') 'tests relevant'
Assert-Equal $true (Test-UpdaterReleaseRelevantPath '.github/workflows/windows-release-gate.yml') 'workflow relevant'
Assert-Equal $true (Test-UpdaterReleaseRelevantPath 'scripts/Build-Release.ps1') 'scripts relevant'
Assert-Equal $true (Test-UpdaterReleaseRelevantPath 'VERSION.txt') 'version relevant'
Assert-Equal $false (Test-UpdaterReleaseRelevantPath '_AGENT_CONTEXT/EVIDENCE/x.log') 'evidence irrelevant'
Assert-Equal $false (Test-UpdaterReleaseRelevantPath '.verification/function-status.json') 'cache irrelevant'

$current='aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa'
$previous='bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb'

$staleUnknown=Get-UpdaterPublicationDecision -CurrentBuild 20 -CurrentSourceSha $current -RemoteMainSha $previous
Assert-Equal $false $staleUnknown.Publish 'unclassified stale main publish'
Assert-Equal 'stale-main-unclassified' $staleUnknown.Reason 'unclassified stale main reason'

$staleControlPlane=Get-UpdaterPublicationDecision -CurrentBuild 20 -CurrentSourceSha $current -RemoteMainSha $previous -RemoteMainChangedPaths @('_AGENT_CONTEXT/CURRENT_STATE.md','tools/agent-control/server.mjs')
Assert-Equal $true $staleControlPlane.Publish 'control-plane-only main drift publish'
Assert-Equal 'release-input-change' $staleControlPlane.Reason 'control-plane-only main drift reason'

$staleProduct=Get-UpdaterPublicationDecision -CurrentBuild 20 -CurrentSourceSha $current -RemoteMainSha $previous -RemoteMainChangedPaths @('_AGENT_CONTEXT/CURRENT_STATE.md','src/MhwModManager.App/App.xaml.cs')
Assert-Equal $false $staleProduct.Publish 'product main drift publish'
Assert-Equal 'stale-main-release-input-change' $staleProduct.Reason 'product main drift reason'

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

$emptyList=ConvertFrom-UpdaterReleaseList -Json ''
Assert-Equal 0 $emptyList.Releases.Count 'empty release-list stdout'

$emptyJsonList=ConvertFrom-UpdaterReleaseList -Json '[]'
Assert-Equal 0 $emptyJsonList.Releases.Count 'empty JSON release list'

$singleRelease=ConvertFrom-UpdaterReleaseList -Json '[{"tagName":"updater-main-42","isDraft":false,"isImmutable":true}]'
Assert-Equal 1 $singleRelease.Releases.Count 'single release list'
Assert-Equal 'updater-main-42' $singleRelease.Releases[0].tagName 'release tag'

foreach($invalidJson in @('{}','null','not-json','[null]','[{"isDraft":false,"isImmutable":true}]')){
  $rejected=$false
  try{[void](ConvertFrom-UpdaterReleaseList -Json $invalidJson)}catch{$rejected=$true}
  Assert-Equal $true $rejected "invalid release-list rejection: $invalidJson"
}

$tagRefJson='{"ref":"refs/tags/updater-main-42","object":{"type":"commit","sha":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"}}'
Assert-Equal $current (Get-UpdaterTagCommitFromRefJson -Json $tagRefJson -ExpectedTag 'updater-main-42') 'published tag REST commit identity'

foreach($invalidTagRef in @(
  '',
  'null',
  'not-json',
  '{"ref":"refs/tags/updater-main-41","object":{"type":"commit","sha":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"}}',
  '{"ref":"refs/tags/updater-main-42"}',
  '{"ref":"refs/tags/updater-main-42","object":{"type":"tag","sha":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"}}',
  '{"ref":"refs/tags/updater-main-42","object":{"type":"commit","sha":"abc"}}'
)){
  $rejected=$false
  try{[void](Get-UpdaterTagCommitFromRefJson -Json $invalidTagRef -ExpectedTag 'updater-main-42')}catch{$rejected=$true}
  Assert-Equal $true $rejected "invalid published tag REST ref rejection: $invalidTagRef"
}

function Invoke-PublicationSequenceFixture {
  param(
    [Parameter(Mandatory=$true)][string]$ExpectedSha,
    [Parameter(Mandatory=$true)][string]$RemoteMainSha,
    [string]$FailStage=''
  )

  $script:UpdaterPublicationFixture=[ordered]@{
    Events=New-Object System.Collections.Generic.List[string]
    RemoteMainSha=$RemoteMainSha
    FailStage=$FailStage
  }
  $result=$null
  $errorMessage=''

  try {
    $result=Invoke-UpdaterDraftPublication -ExpectedSourceSha $ExpectedSha `
      -CreateDraft {
        $script:UpdaterPublicationFixture.Events.Add('create')
        if($script:UpdaterPublicationFixture.FailStage -eq 'create'){throw 'fixture-create'}
      } `
      -UploadAssets {
        $script:UpdaterPublicationFixture.Events.Add('upload')
        if($script:UpdaterPublicationFixture.FailStage -eq 'upload'){throw 'fixture-upload'}
      } `
      -VerifyDraft {
        $script:UpdaterPublicationFixture.Events.Add('verify')
        if($script:UpdaterPublicationFixture.FailStage -eq 'verify'){throw 'fixture-verify'}
      } `
      -RefreshMain {
        $script:UpdaterPublicationFixture.Events.Add('refresh')
        if($script:UpdaterPublicationFixture.FailStage -eq 'refresh'){throw 'fixture-refresh'}
        return [string]$script:UpdaterPublicationFixture.RemoteMainSha
      } `
      -DeleteDraft {
        $script:UpdaterPublicationFixture.Events.Add('delete')
        if($script:UpdaterPublicationFixture.FailStage -eq 'delete'){throw 'fixture-delete'}
      } `
      -PublishDraft {
        $script:UpdaterPublicationFixture.Events.Add('publish')
        if($script:UpdaterPublicationFixture.FailStage -eq 'publish'){throw 'fixture-publish'}
      }
  }
  catch {
    $errorMessage=$_.Exception.Message
  }

  return [pscustomobject]@{
    Result=$result
    Error=$errorMessage
    Events=(@($script:UpdaterPublicationFixture.Events) -join '|')
  }
}

$sequenceSuccess=Invoke-PublicationSequenceFixture -ExpectedSha $current -RemoteMainSha $current
Assert-Equal '' $sequenceSuccess.Error 'draft sequence success error'
Assert-Equal $true $sequenceSuccess.Result.Published 'draft sequence success publish'
Assert-Equal 'published' $sequenceSuccess.Result.Reason 'draft sequence success reason'
Assert-Equal 'create|upload|verify|refresh|publish' $sequenceSuccess.Events 'draft sequence success order'

$sequenceStale=Invoke-PublicationSequenceFixture -ExpectedSha $current -RemoteMainSha $previous
Assert-Equal '' $sequenceStale.Error 'post-upload stale main error'
Assert-Equal $false $sequenceStale.Result.Published 'post-upload stale main publish'
Assert-Equal 'stale-main-after-upload' $sequenceStale.Result.Reason 'post-upload stale main reason'
Assert-Equal 'create|upload|verify|refresh|delete' $sequenceStale.Events 'post-upload stale main cleanup order'

$script:UpdaterPublicationDriftEvents=New-Object System.Collections.Generic.List[string]
$sequenceIrrelevantDrift=Invoke-UpdaterDraftPublication -ExpectedSourceSha $current `
  -CreateDraft {$script:UpdaterPublicationDriftEvents.Add('create')} `
  -UploadAssets {$script:UpdaterPublicationDriftEvents.Add('upload')} `
  -VerifyDraft {$script:UpdaterPublicationDriftEvents.Add('verify')} `
  -RefreshMain {$script:UpdaterPublicationDriftEvents.Add('refresh'); $previous} `
  -DeleteDraft {$script:UpdaterPublicationDriftEvents.Add('delete')} `
  -PublishDraft {$script:UpdaterPublicationDriftEvents.Add('publish')} `
  -EvaluateRefreshedMain {param($remoteMain) [pscustomobject]@{Publish=$true;Reason='release-inputs-unchanged'}}
Assert-Equal $true $sequenceIrrelevantDrift.Published 'post-upload irrelevant drift publish'
Assert-Equal 'create|upload|verify|refresh|publish' (@($script:UpdaterPublicationDriftEvents) -join '|') 'post-upload irrelevant drift order'

$script:UpdaterPublicationRelevantDriftEvents=New-Object System.Collections.Generic.List[string]
$sequenceRelevantDrift=Invoke-UpdaterDraftPublication -ExpectedSourceSha $current `
  -CreateDraft {$script:UpdaterPublicationRelevantDriftEvents.Add('create')} `
  -UploadAssets {$script:UpdaterPublicationRelevantDriftEvents.Add('upload')} `
  -VerifyDraft {$script:UpdaterPublicationRelevantDriftEvents.Add('verify')} `
  -RefreshMain {$script:UpdaterPublicationRelevantDriftEvents.Add('refresh'); $previous} `
  -DeleteDraft {$script:UpdaterPublicationRelevantDriftEvents.Add('delete')} `
  -PublishDraft {$script:UpdaterPublicationRelevantDriftEvents.Add('publish')} `
  -EvaluateRefreshedMain {param($remoteMain) [pscustomobject]@{Publish=$false;Reason='stale-main-release-input-change'}}
Assert-Equal $false $sequenceRelevantDrift.Published 'post-upload relevant drift publish'
Assert-Equal 'stale-main-release-input-change' $sequenceRelevantDrift.Reason 'post-upload relevant drift reason'
Assert-Equal 'create|upload|verify|refresh|delete' (@($script:UpdaterPublicationRelevantDriftEvents) -join '|') 'post-upload relevant drift cleanup order'

$sequenceCreateFailure=Invoke-PublicationSequenceFixture -ExpectedSha $current -RemoteMainSha $current -FailStage 'create'
Assert-Equal 'fixture-create' $sequenceCreateFailure.Error 'draft creation failure propagated'
Assert-Equal 'create' $sequenceCreateFailure.Events 'draft creation ambiguity avoids unsafe cleanup or publish'

$sequenceUploadFailure=Invoke-PublicationSequenceFixture -ExpectedSha $current -RemoteMainSha $current -FailStage 'upload'
Assert-Equal 'fixture-upload' $sequenceUploadFailure.Error 'upload failure propagated'
Assert-Equal 'create|upload|delete' $sequenceUploadFailure.Events 'upload failure draft cleanup'

$sequenceVerifyFailure=Invoke-PublicationSequenceFixture -ExpectedSha $current -RemoteMainSha $current -FailStage 'verify'
Assert-Equal 'fixture-verify' $sequenceVerifyFailure.Error 'draft verification failure propagated'
Assert-Equal 'create|upload|verify|delete' $sequenceVerifyFailure.Events 'draft verification failure cleanup'

$sequenceRefreshFailure=Invoke-PublicationSequenceFixture -ExpectedSha $current -RemoteMainSha $current -FailStage 'refresh'
Assert-Equal 'fixture-refresh' $sequenceRefreshFailure.Error 'final main refresh failure propagated'
Assert-Equal 'create|upload|verify|refresh|delete' $sequenceRefreshFailure.Events 'final main refresh failure cleanup'

$sequenceDeleteFailure=Invoke-PublicationSequenceFixture -ExpectedSha $current -RemoteMainSha $previous -FailStage 'delete'
Assert-Equal 'fixture-delete' $sequenceDeleteFailure.Error 'stale draft cleanup failure propagated'
Assert-Equal 'create|upload|verify|refresh|delete' $sequenceDeleteFailure.Events 'stale draft cleanup failure is not retried blindly'

$sequencePublishFailure=Invoke-PublicationSequenceFixture -ExpectedSha $current -RemoteMainSha $current -FailStage 'publish'
Assert-Equal 'fixture-publish' $sequencePublishFailure.Error 'publish failure propagated'
Assert-Equal 'create|upload|verify|refresh|publish' $sequencePublishFailure.Events 'publish failure avoids unsafe automatic deletion'

$noisySequence=Invoke-UpdaterDraftPublication -ExpectedSourceSha $current `
  -CreateDraft {'create-output'} `
  -UploadAssets {'upload-output'} `
  -VerifyDraft {'verify-output'} `
  -RefreshMain {$current} `
  -DeleteDraft {'delete-output'} `
  -PublishDraft {'publish-output'}
Assert-Equal 1 (@($noisySequence).Count) 'publication callback output suppression'
Assert-Equal $true $noisySequence.Published 'publication callback output suppression result'

$script:UpdaterRefreshCleanupCount=0
$multiRefreshError=''
try {
  [void](Invoke-UpdaterDraftPublication -ExpectedSourceSha $current `
    -CreateDraft {} `
    -UploadAssets {} `
    -VerifyDraft {} `
    -RefreshMain {@($current,$previous)} `
    -DeleteDraft {$script:UpdaterRefreshCleanupCount++} `
    -PublishDraft {})
}
catch {
  $multiRefreshError=$_.Exception.Message
}
Assert-Equal 1 $script:UpdaterRefreshCleanupCount 'ambiguous final main refresh cleanup'
Assert-Equal $true ($multiRefreshError -like 'Final updater publication main refresh returned *') 'ambiguous final main refresh rejection'

# Regression for the retry-path propagation race: both the immediate
# post-publication verification and an already-immutable release retry must use
# the authoritative Git REST ref. The one remaining local current-build tag
# check is deliberately the pre-publication orphan-tag refusal.
$publishSource=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'Publish-UpdaterRelease.ps1') -Raw
$restCurrentTagChecks=[regex]::Matches($publishSource,'gh api "repos/\$Repository/git/ref/tags/\$tag"').Count
Assert-Equal 2 $restCurrentTagChecks 'new and existing immutable release REST tag verification'
$localCurrentTagChecks=[regex]::Matches($publishSource,'git show-ref --verify --quiet "refs/tags/\$tag"').Count
Assert-Equal 1 $localCurrentTagChecks 'only orphan-tag refusal uses local current-build tag'

Write-Host 'PASS: updater release publication policy' -ForegroundColor Green
