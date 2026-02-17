param(
  [Parameter(Mandatory = $true)]
  [string]$EvidenceRoot,

  [string]$ReportPath = "",

  [string[]]$AdditionalRequiredArtifacts = @(),

  [string[]]$AdditionalRequiredSummarySteps = @(),

  [string[]]$DisabledChecks = @(),

  [string]$RecoveryCommand = "powershell -NoProfile -ExecutionPolicy Bypass -File .\\tools\\release\\Invoke-ReleaseGate.ps1 -Configuration Release -OutputRoot .\\.artifacts\\release-gate\\latest"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function New-ContractRequirement {
  param(
    [Parameter(Mandatory = $true)][string]$Key,
    [Parameter(Mandatory = $true)][string]$RelativePath,
    [bool]$Required = $true,
    [string]$SummaryProperty = "",
    [string]$ExpectedValue = "",
    [string]$SummaryStep = ""
  )

  return [pscustomobject]@{
    key = $Key
    relativePath = $RelativePath
    required = $Required
    summaryProperty = $SummaryProperty
    expectedValue = $ExpectedValue
    summaryStep = $SummaryStep
  }
}

function Resolve-RequirementPath {
  param(
    [Parameter(Mandatory = $true)][string]$Root,
    [Parameter(Mandatory = $true)][string]$RelativePath
  )

  return [IO.Path]::GetFullPath((Join-Path $Root $RelativePath))
}

function Get-NestedPropertyValue {
  param(
    [Parameter(Mandatory = $true)][object]$InputObject,
    [Parameter(Mandatory = $true)][string]$PropertyPath
  )

  $segments = $PropertyPath.Split('.')
  $current = $InputObject

  foreach ($segment in $segments) {
    if ($null -eq $current) {
      return $null
    }

    $property = $current.PSObject.Properties[$segment]
    if ($null -eq $property) {
      return $null
    }

    $current = $property.Value
  }

  return $current
}

function Add-Blocker {
  param(
    [Parameter(Mandatory = $true)][string]$Category,
    [Parameter(Mandatory = $true)][string]$WhatBlocked,
    [Parameter(Mandatory = $true)][string]$WhyBlocked,
    [Parameter(Mandatory = $true)][string]$NextCommand
  )

  $script:Blockers += [pscustomobject]@{
    category = $Category
    whatBlocked = $WhatBlocked
    whyBlocked = $WhyBlocked
    nextCommand = $NextCommand
  }
}

function Write-ContractReport {
  param(
    [Parameter(Mandatory = $true)][string]$Path,
    [Parameter(Mandatory = $true)][object[]]$Blockers,
    [Parameter(Mandatory = $true)][string]$Root
  )

  $report = New-Object System.Text.StringBuilder
  [void]$report.AppendLine("# Release Evidence Contract Report")
  [void]$report.AppendLine()
  [void]$report.AppendLine("- generatedAtUtc: $((Get-Date).ToUniversalTime().ToString('o'))")
  [void]$report.AppendLine("- evidenceRoot: $Root")
  [void]$report.AppendLine("- blockerCount: $($Blockers.Count)")
  [void]$report.AppendLine()

  if ($Blockers.Count -eq 0) {
    [void]$report.AppendLine("## Contract Status")
    [void]$report.AppendLine()
    [void]$report.AppendLine("- what blocked: none")
    [void]$report.AppendLine("- why blocked: none")
    [void]$report.AppendLine("- next command: none")
  }
  else {
    [void]$report.AppendLine("## Blockers")
    [void]$report.AppendLine()

    $index = 1
    foreach ($blocker in $Blockers) {
      [void]$report.AppendLine("### $index. [$($blocker.category)]")
      [void]$report.AppendLine("- what blocked: $($blocker.whatBlocked)")
      [void]$report.AppendLine("- why blocked: $($blocker.whyBlocked)")
      [void]$report.AppendLine("- next command: $($blocker.nextCommand)")
      [void]$report.AppendLine()
      $index += 1
    }
  }

  Set-Content -Path $Path -Value $report.ToString() -Encoding UTF8
}

$evidenceRootFull = [IO.Path]::GetFullPath($EvidenceRoot)
if ([string]::IsNullOrWhiteSpace($ReportPath)) {
  $ReportPath = Join-Path $evidenceRootFull "report/release-evidence-contract-report.md"
}

$reportPathFull = [IO.Path]::GetFullPath($ReportPath)
$reportDirectory = Split-Path -Parent $reportPathFull
New-Item -ItemType Directory -Force -Path $reportDirectory | Out-Null

$script:Blockers = @()

$coreRequirements = @(
  (New-ContractRequirement -Key "releaseGateSummary" -RelativePath "report/release-gate-summary.json" -SummaryProperty "overallPassed" -ExpectedValue "True"),
  (New-ContractRequirement -Key "releaseGateReport" -RelativePath "report/release-gate-report.md"),
  (New-ContractRequirement -Key "releaseGateChecksums" -RelativePath "report/sha256-checksums.txt"),
  (New-ContractRequirement -Key "upgradeRebaseSummary" -RelativePath "upgrade-rebase/upgrade-rebase-summary.json" -SummaryProperty "status" -ExpectedValue "passed"),
  (New-ContractRequirement -Key "upgradeRebaseReport" -RelativePath "upgrade-rebase/upgrade-rebase-report.md"),
  (New-ContractRequirement -Key "upgradeRebaseWpfWorkflowContract" -RelativePath "upgrade-rebase/upgrade-rebase-summary.json" -SummaryStep "upgrade-rebase-wpf-workflow-contract"),
  (New-ContractRequirement -Key "upgradeRebaseWpfSeverityContract" -RelativePath "upgrade-rebase/upgrade-rebase-summary.json" -SummaryStep "upgrade-rebase-wpf-severity-contract"),
  (New-ContractRequirement -Key "upgradeRebaseWpfRecoveryContract" -RelativePath "upgrade-rebase/upgrade-rebase-summary.json" -SummaryStep "upgrade-rebase-wpf-recovery-contract"),
  (New-ContractRequirement -Key "quarterlySummary" -RelativePath "quarterly-pack/quarterly-pack-summary.json" -SummaryProperty "overallPassed" -ExpectedValue "True"),
  (New-ContractRequirement -Key "quarterlyReport" -RelativePath "quarterly-pack/quarterly-pack-report.md")
)

$requirements = @($coreRequirements)

foreach ($artifact in $AdditionalRequiredArtifacts) {
  if (-not [string]::IsNullOrWhiteSpace($artifact)) {
    $requirements += New-ContractRequirement -Key $artifact -RelativePath $artifact
  }
}

$summaryCache = @{}
foreach ($requirement in $requirements) {
  $fullPath = Resolve-RequirementPath -Root $evidenceRootFull -RelativePath $requirement.relativePath
  $exists = Test-Path -LiteralPath $fullPath

  if (-not $exists) {
    Add-Blocker -Category "missing-proof" -WhatBlocked $requirement.key -WhyBlocked "Required evidence artifact is missing at '$($requirement.relativePath)'." -NextCommand $RecoveryCommand
    continue
  }

  if (-not [string]::IsNullOrWhiteSpace($requirement.summaryProperty) -or -not [string]::IsNullOrWhiteSpace($requirement.summaryStep)) {
    $cacheKey = $fullPath.ToLowerInvariant()
    if (-not $summaryCache.ContainsKey($cacheKey)) {
      try {
        $summaryCache[$cacheKey] = Get-Content -Path $fullPath -Raw | ConvertFrom-Json
      }
      catch {
        Add-Blocker -Category "failed-check" -WhatBlocked $requirement.key -WhyBlocked "Summary payload '$($requirement.relativePath)' is unreadable: $($_.Exception.Message)" -NextCommand $RecoveryCommand
        continue
      }
    }

    $summaryPayload = $summaryCache[$cacheKey]
    if (-not [string]::IsNullOrWhiteSpace($requirement.summaryProperty)) {
      $actualValue = Get-NestedPropertyValue -InputObject $summaryPayload -PropertyPath $requirement.summaryProperty
      if ($null -eq $actualValue -or ([string]$actualValue) -ne $requirement.expectedValue) {
        Add-Blocker -Category "failed-check" -WhatBlocked $requirement.key -WhyBlocked "Expected '$($requirement.summaryProperty)' to be '$($requirement.expectedValue)' in '$($requirement.relativePath)' but was '$actualValue'." -NextCommand $RecoveryCommand
      }
    }

    if (-not [string]::IsNullOrWhiteSpace($requirement.summaryStep)) {
      $matchingStep = @($summaryPayload.steps | Where-Object { $_.name -eq $requirement.summaryStep })
      if ($matchingStep.Count -ne 1) {
        Add-Blocker -Category "missing-proof" -WhatBlocked $requirement.key -WhyBlocked "Required summary step '$($requirement.summaryStep)' is missing in '$($requirement.relativePath)'." -NextCommand $RecoveryCommand
      }
      elseif (-not [bool]$matchingStep[0].succeeded) {
        Add-Blocker -Category "failed-check" -WhatBlocked $requirement.key -WhyBlocked "Summary step '$($requirement.summaryStep)' failed in '$($requirement.relativePath)'." -NextCommand $RecoveryCommand
      }
    }
  }
}

foreach ($summaryStep in $AdditionalRequiredSummarySteps) {
  if ([string]::IsNullOrWhiteSpace($summaryStep)) {
    continue
  }

  $summaryPath = Resolve-RequirementPath -Root $evidenceRootFull -RelativePath "report/release-gate-summary.json"
  if (-not (Test-Path -LiteralPath $summaryPath)) {
    Add-Blocker -Category "missing-proof" -WhatBlocked "summary-step:$summaryStep" -WhyBlocked "Cannot validate summary step because report/release-gate-summary.json is missing." -NextCommand $RecoveryCommand
    continue
  }

  $cacheKey = $summaryPath.ToLowerInvariant()
  if (-not $summaryCache.ContainsKey($cacheKey)) {
    try {
      $summaryCache[$cacheKey] = Get-Content -Path $summaryPath -Raw | ConvertFrom-Json
    }
    catch {
      Add-Blocker -Category "failed-check" -WhatBlocked "summary-step:$summaryStep" -WhyBlocked "Unable to parse report/release-gate-summary.json: $($_.Exception.Message)" -NextCommand $RecoveryCommand
      continue
    }
  }

  $summaryPayload = $summaryCache[$cacheKey]
  $matchingStep = @($summaryPayload.steps | Where-Object { $_.name -eq $summaryStep })
  if ($matchingStep.Count -ne 1) {
    Add-Blocker -Category "missing-proof" -WhatBlocked "summary-step:$summaryStep" -WhyBlocked "Required release summary step '$summaryStep' is missing." -NextCommand $RecoveryCommand
  }
  elseif (-not [bool]$matchingStep[0].succeeded) {
    Add-Blocker -Category "failed-check" -WhatBlocked "summary-step:$summaryStep" -WhyBlocked "Required release summary step '$summaryStep' is not passing." -NextCommand $RecoveryCommand
  }
}

foreach ($disabledCheck in $DisabledChecks) {
  if ([string]::IsNullOrWhiteSpace($disabledCheck)) {
    continue
  }

  Add-Blocker -Category "disabled-check" -WhatBlocked $disabledCheck -WhyBlocked "Required promotion check is disabled by configuration." -NextCommand $RecoveryCommand
}

if ($script:Blockers.Count -gt 0) {
  foreach ($blocker in $script:Blockers) {
    Write-Host "[$($blocker.category)]" -ForegroundColor Red
    Write-Host "- what blocked: $($blocker.whatBlocked)"
    Write-Host "- why blocked: $($blocker.whyBlocked)"
    Write-Host "- next command: $($blocker.nextCommand)"
    Write-Host ""
  }

  Write-ContractReport -Path $reportPathFull -Blockers $script:Blockers -Root $evidenceRootFull
  $categoryList = @($script:Blockers | ForEach-Object { $_.category } | Select-Object -Unique)
  $categoryText = $categoryList -join ","
  throw "[$categoryText] Release evidence contract blocked. See $reportPathFull"
}

Write-ContractReport -Path $reportPathFull -Blockers @() -Root $evidenceRootFull
Write-Host "Release evidence contract validation passed. Report: $reportPathFull" -ForegroundColor Green
