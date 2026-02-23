param(
  [string]$CurrentResultPath = "",
  [string]$PolicyPath = "",
  [switch]$Enforce
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Resolve-FromRepositoryRoot {
  param(
    [Parameter(Mandatory = $true)][string]$RepositoryRoot,
    [Parameter(Mandatory = $true)][string]$Candidate
  )

  if ([IO.Path]::IsPathRooted($Candidate)) {
    return [IO.Path]::GetFullPath($Candidate)
  }

  return [IO.Path]::GetFullPath((Join-Path $RepositoryRoot $Candidate))
}

function Get-NumericValue {
  param(
    [Parameter(Mandatory = $true)][string]$Name,
    [Parameter(Mandatory = $true)]$RawValue
  )

  $parsed = 0.0
  if (-not [double]::TryParse([string]$RawValue, [Globalization.NumberStyles]::Float, [Globalization.CultureInfo]::InvariantCulture, [ref]$parsed)) {
    throw "$Name must be numeric, found '$RawValue'."
  }

  if ($parsed -lt 0 -or $parsed -gt 100) {
    throw "$Name must be between 0 and 100 (inclusive), found '$RawValue'."
  }

  return $parsed
}

function Get-ObjectPropertyValue {
  param(
    [Parameter(Mandatory = $true)]$Object,
    [Parameter(Mandatory = $true)][string]$PropertyName
  )

  if ($null -eq $Object) {
    return $null
  }

  $property = $Object.PSObject.Properties[$PropertyName]
  if ($null -eq $property) {
    return $null
  }

  return $property.Value
}

function Resolve-MutationScore {
  param(
    [Parameter(Mandatory = $true)]$CurrentResult
  )

  $scoreRaw = Get-ObjectPropertyValue -Object $CurrentResult -PropertyName "mutationScore"
  if ($null -eq $scoreRaw) {
    $metrics = Get-ObjectPropertyValue -Object $CurrentResult -PropertyName "metrics"
    $scoreRaw = Get-ObjectPropertyValue -Object $metrics -PropertyName "mutationScore"
  }

  if ($null -eq $scoreRaw) {
    $summary = Get-ObjectPropertyValue -Object $CurrentResult -PropertyName "summary"
    $scoreRaw = Get-ObjectPropertyValue -Object $summary -PropertyName "mutationScore"
  }

  if ($null -eq $scoreRaw -or [string]::IsNullOrWhiteSpace([string]$scoreRaw)) {
    throw "Current mutation result must contain mutationScore (root, metrics.mutationScore, or summary.mutationScore)."
  }

  return Get-NumericValue -Name "Current mutation score" -RawValue $scoreRaw
}

function Format-Percent {
  param([double]$Value)

  return $Value.ToString("0.##", [Globalization.CultureInfo]::InvariantCulture)
}

function Invoke-MutationPolicy {
  param(
    [Parameter(Mandatory = $true)][string]$CurrentMutationResultPath,
    [Parameter(Mandatory = $true)][string]$MutationPolicyPath,
    [Parameter(Mandatory = $true)][bool]$EnableEnforcement
  )

  if (-not (Test-Path -LiteralPath $MutationPolicyPath)) {
    throw "Mutation policy file not found: $MutationPolicyPath"
  }

  if (-not (Test-Path -LiteralPath $CurrentMutationResultPath)) {
    throw "Current mutation result file not found: $CurrentMutationResultPath"
  }

  $policy = Get-Content -Path $MutationPolicyPath -Raw | ConvertFrom-Json
  $baselineRaw = Get-ObjectPropertyValue -Object $policy -PropertyName "baselineMutationScore"
  if ($null -eq $baselineRaw -or [string]::IsNullOrWhiteSpace([string]$baselineRaw)) {
    throw "Mutation policy baselineMutationScore is required."
  }

  $allowedRegressionRaw = Get-ObjectPropertyValue -Object $policy -PropertyName "allowedRegression"
  if ($null -eq $allowedRegressionRaw -or [string]::IsNullOrWhiteSpace([string]$allowedRegressionRaw)) {
    throw "Mutation policy allowedRegression is required."
  }

  $baselineMutationScore = Get-NumericValue -Name "Mutation policy baselineMutationScore" -RawValue $baselineRaw
  $allowedRegression = Get-NumericValue -Name "Mutation policy allowedRegression" -RawValue $allowedRegressionRaw
  $currentResult = Get-Content -Path $CurrentMutationResultPath -Raw | ConvertFrom-Json
  $currentMutationScore = Resolve-MutationScore -CurrentResult $currentResult

  $minimumAllowedScore = [Math]::Max(0.0, $baselineMutationScore - $allowedRegression)
  $mode = if ($EnableEnforcement) { "enforce" } else { "report" }
  $diagnostic = "mode=$mode baseline=$(Format-Percent -Value $baselineMutationScore) allowedRegression=$(Format-Percent -Value $allowedRegression) minimumAllowed=$(Format-Percent -Value $minimumAllowedScore) current=$(Format-Percent -Value $currentMutationScore)"

  if (-not $EnableEnforcement) {
    Write-Host "[mutation-policy] Mutation policy report: $diagnostic" -ForegroundColor Yellow
    return 0
  }

  if ($currentMutationScore -lt $minimumAllowedScore) {
    Write-Host "[mutation-policy] Mutation policy failed: $diagnostic" -ForegroundColor Red
    return 1
  }

  Write-Host "[mutation-policy] Mutation policy passed: $diagnostic" -ForegroundColor Green
  return 0
}

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repositoryRoot = (Resolve-Path (Join-Path $scriptRoot "..\..")).Path

if ([string]::IsNullOrWhiteSpace($PolicyPath)) {
  $PolicyPath = Join-Path $scriptRoot "mutation-policy.json"
}

if ([string]::IsNullOrWhiteSpace($CurrentResultPath)) {
  $CurrentResultPath = ".artifacts\mutation\current-result.json"
}

$policyPathFull = Resolve-FromRepositoryRoot -RepositoryRoot $repositoryRoot -Candidate $PolicyPath
$currentResultPathFull = Resolve-FromRepositoryRoot -RepositoryRoot $repositoryRoot -Candidate $CurrentResultPath

try {
  $exitCode = Invoke-MutationPolicy -CurrentMutationResultPath $currentResultPathFull -MutationPolicyPath $policyPathFull -EnableEnforcement:$Enforce.IsPresent
  exit $exitCode
}
catch {
  Write-Host "[mutation-policy] Mutation policy failed: $($_.Exception.Message)" -ForegroundColor Red
  exit 1
}
