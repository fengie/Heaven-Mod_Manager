Set-StrictMode -Version Latest

function Invoke-UpdaterDraftPublication {
  [CmdletBinding()]
  param(
    [Parameter(Mandatory=$true)][string]$ExpectedSourceSha,
    [Parameter(Mandatory=$true)][scriptblock]$CreateDraft,
    [Parameter(Mandatory=$true)][scriptblock]$UploadAssets,
    [Parameter(Mandatory=$true)][scriptblock]$VerifyDraft,
    [Parameter(Mandatory=$true)][scriptblock]$RefreshMain,
    [Parameter(Mandatory=$true)][scriptblock]$DeleteDraft,
    [Parameter(Mandatory=$true)][scriptblock]$PublishDraft,
    [scriptblock]$EvaluateRefreshedMain=$null
  )

  $draftCreated=$false
  $cleanupAttempted=$false
  $publicationAttempted=$false

  try {
    $null = & $CreateDraft
    $draftCreated=$true

    $null = & $UploadAssets
    $null = & $VerifyDraft

    $refreshOutput=@(& $RefreshMain)
    if($refreshOutput.Count -ne 1){
      throw "Final updater publication main refresh returned $($refreshOutput.Count) output values; expected exactly one revision."
    }
    $remoteMain=[string]$refreshOutput[0]
    if([string]::IsNullOrWhiteSpace($remoteMain)){
      throw 'Final updater publication main refresh returned no revision.'
    }
    $remoteMain=$remoteMain.Trim()

    if(-not [string]::Equals($remoteMain,$ExpectedSourceSha,[StringComparison]::OrdinalIgnoreCase)){
      if($null -eq $EvaluateRefreshedMain){
        $mainDecision=[pscustomobject]@{Publish=$false;Reason='stale-main-after-upload'}
      } else {
        $decisionOutput=@(& $EvaluateRefreshedMain $remoteMain)
        if($decisionOutput.Count -ne 1){
          throw "Updater publication main-drift evaluator returned $($decisionOutput.Count) values; expected exactly one decision."
        }
        $mainDecision=$decisionOutput[0]
        if($null -eq $mainDecision -or $null -eq $mainDecision.PSObject.Properties['Publish'] -or $null -eq $mainDecision.PSObject.Properties['Reason']){
          throw 'Updater publication main-drift evaluator returned an invalid decision.'
        }
      }
      if(-not [bool]$mainDecision.Publish){
        $cleanupAttempted=$true
        $null = & $DeleteDraft
        $draftCreated=$false
        return [pscustomobject]@{
          Published=$false
          Reason=[string]$mainDecision.Reason
          RemoteMainSha=$remoteMain
        }
      }
    }

    # Once publication starts, a command failure is state-ambiguous: GitHub may have
    # published the immutable release even if the client process reports failure.
    # Never automatically delete after crossing this boundary.
    $publicationAttempted=$true
    $null = & $PublishDraft
    $draftCreated=$false

    return [pscustomobject]@{
      Published=$true
      Reason='published'
      RemoteMainSha=$remoteMain
    }
  }
  catch {
    $failure=$_
    if($draftCreated -and -not $publicationAttempted -and -not $cleanupAttempted){
      $cleanupAttempted=$true
      try {
        $null = & $DeleteDraft
        $draftCreated=$false
      }
      catch {
        $cleanupFailure=$_
        throw "Updater publication failed before publication and draft cleanup also failed. Original failure: $($failure.Exception.Message) Cleanup failure: $($cleanupFailure.Exception.Message)"
      }
    }
    throw $failure
  }
}
