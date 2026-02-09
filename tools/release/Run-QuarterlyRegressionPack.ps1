param(
  [string]$PackPath = "",
  [string]$OutputRoot = "",
  [string]$RepositoryRoot = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Resolve-FromRepositoryRoot {
  param(
    [Parameter(Mandatory = $true)][string]$Root,
    [Parameter(Mandatory = $true)][string]$Candidate
  )

  if ([IO.Path]::IsPathRooted($Candidate)) {
    return [IO.Path]::GetFullPath($Candidate)
  }

  return [IO.Path]::GetFullPath((Join-Path $Root $Candidate))
}

function Convert-ToRelativePath {
  param(
    [Parameter(Mandatory = $true)][string]$BasePath,
    [Parameter(Mandatory = $true)][string]$TargetPath
  )

  $baseResolved = (Resolve-Path $BasePath).Path
  $targetResolved = [IO.Path]::GetFullPath($TargetPath)

  $baseSuffix = if ($baseResolved.EndsWith([IO.Path]::DirectorySeparatorChar) -or $baseResolved.EndsWith([IO.Path]::AltDirectorySeparatorChar)) { "" } else { [string][IO.Path]::DirectorySeparatorChar }
  $baseUri = [System.Uri]::new(($baseResolved + $baseSuffix), [System.UriKind]::Absolute)
  $targetUri = [System.Uri]::new($targetResolved, [System.UriKind]::Absolute)

  return [System.Uri]::UnescapeDataString($baseUri.MakeRelativeUri($targetUri).ToString().Replace('/', [IO.Path]::DirectorySeparatorChar))
}

function Get-FileProfile {
  param(
    [Parameter(Mandatory = $true)][string]$Path
  )

  $rawContent = Get-Content -LiteralPath $Path -Raw -ErrorAction Stop
  $canonicalContent = $rawContent -replace "`r`n", "`n" -replace "`r", "`n"
  $content = @(Get-Content -LiteralPath $Path -ErrorAction Stop)

  $sha = [System.Security.Cryptography.SHA256]::Create()
  try {
    $canonicalBytes = [System.Text.Encoding]::UTF8.GetBytes($canonicalContent)
    $hashBytes = $sha.ComputeHash($canonicalBytes)
  }
  finally {
    $sha.Dispose()
  }

  $hash = ([System.BitConverter]::ToString($hashBytes)).Replace("-", "").ToLowerInvariant()
  $sizeBytes = [int64]$canonicalBytes.Length

  $rootElement = ""
  $rootElementError = ""
  $rawXml = ""

  try {
    $rawXml = $rawContent
    [xml]$xml = $rawXml
    if ($null -ne $xml.DocumentElement) {
      $rootElement = $xml.DocumentElement.LocalName
    }
  }
  catch {
    $rootElementError = $_.Exception.Message
  }

  return [pscustomobject]@{
    sha256 = $hash
    sizeBytes = $sizeBytes
    lineCount = @($content).Count
    rootElement = $rootElement
    rootElementError = $rootElementError
  }
}

function Get-PercentDelta {
  param(
    [Parameter(Mandatory = $true)][double]$Baseline,
    [Parameter(Mandatory = $true)][double]$Current
  )

  if ($Baseline -eq 0) {
    if ($Current -eq 0) {
      return 0.0
    }

    return 100.0
  }

  return [Math]::Round((([Math]::Abs($Current - $Baseline) / $Baseline) * 100.0), 2)
}

function Get-OptionalPropertyValue {
  param(
    [Parameter(Mandatory = $true)]$Object,
    [Parameter(Mandatory = $true)][string]$Name,
    [Parameter()][object]$Default = $null
  )

  if ($null -eq $Object) {
    return $Default
  }

  if ($Object -is [System.Collections.IDictionary]) {
    if ($Object.Contains($Name)) {
      return $Object[$Name]
    }

    return $Default
  }

  $property = $Object.PSObject.Properties[$Name]
  if ($null -eq $property) {
    return $Default
  }

  return $property.Value
}

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path

if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) {
  $RepositoryRoot = (Resolve-Path (Join-Path $scriptRoot "..\..")).Path
}
else {
  $RepositoryRoot = (Resolve-Path $RepositoryRoot).Path
}

if ([string]::IsNullOrWhiteSpace($PackPath)) {
  $PackPath = Join-Path $scriptRoot "quarterly-regression-pack.psd1"
}
$packFullPath = Resolve-FromRepositoryRoot -Root $RepositoryRoot -Candidate $PackPath

if (-not (Test-Path -LiteralPath $packFullPath)) {
  throw "Quarterly regression pack manifest not found: $packFullPath"
}

if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
  $stamp = Get-Date -Format "yyyyMMdd-HHmmss"
  $OutputRoot = Join-Path $RepositoryRoot ".artifacts\quarterly-pack\$stamp"
}
$outputFullPath = Resolve-FromRepositoryRoot -Root $RepositoryRoot -Candidate $OutputRoot

New-Item -ItemType Directory -Path $outputFullPath -Force | Out-Null

$pack = Import-PowerShellDataFile -Path $packFullPath
if ($null -eq $pack) {
  throw "Failed to load quarterly pack manifest from $packFullPath"
}

$schemaVersion = [int]$pack.SchemaVersion
if ($schemaVersion -ne 1) {
  throw "Unsupported quarterly pack schema version '$schemaVersion'."
}

$driftPolicy = $pack.DriftPolicy
if ($null -eq $driftPolicy) {
  throw "Manifest is missing DriftPolicy."
}

$fixtures = @($pack.Fixtures)
if ($fixtures.Count -eq 0) {
  throw "Manifest contains no fixtures."
}

$profilesByScenario = @{}
$entries = New-Object System.Collections.Generic.List[object]
$warnings = New-Object System.Collections.Generic.List[object]
$failures = New-Object System.Collections.Generic.List[object]

foreach ($fixture in ($fixtures | Sort-Object { [string]$_.Scenario })) {
  $scenario = [string](Get-OptionalPropertyValue -Object $fixture -Name "Scenario" -Default "")
  $format = [string](Get-OptionalPropertyValue -Object $fixture -Name "Format" -Default "")
  $fixtureRelativePath = [string](Get-OptionalPropertyValue -Object $fixture -Name "Path" -Default "")
  $fixturePath = Resolve-FromRepositoryRoot -Root $RepositoryRoot -Candidate $fixtureRelativePath
  $exists = Test-Path -LiteralPath $fixturePath

  $result = [ordered]@{
    scenario = $scenario
    format = $format
    fixturePath = $fixturePath
    fixturePathRelative = $fixtureRelativePath
    exists = [bool]$exists
    status = "pass"
    findings = @()
    profile = $null
    baseline = $null
    comparison = $null
  }

  if (-not $exists) {
    $severity = if ([bool]$driftPolicy.FailOnMissingFixture) { "fail" } else { "warning" }
    $finding = [pscustomobject]@{
        severity = $severity
        code = "missing-fixture"
        message = "Fixture file is missing."
        expectedPath = $fixtureRelativePath
      }
    $result.findings += $finding
    if ($severity -eq "fail") {
      $result.status = "fail"
      $failures.Add([pscustomobject]@{ scenario = $scenario; code = $finding.code; message = $finding.message })
    }
    else {
      $result.status = "warning"
      $warnings.Add([pscustomobject]@{ scenario = $scenario; code = $finding.code; message = $finding.message })
    }

    $entries.Add([pscustomobject]$result)
    continue
  }

  $profile = Get-FileProfile -Path $fixturePath
  $profilesByScenario[$scenario] = $profile
  $result.profile = $profile

  $fixtureBaseline = Get-OptionalPropertyValue -Object $fixture -Name "Baseline"
  if ($null -ne $fixtureBaseline) {
    $expectedBaseline = [pscustomobject]@{
      sha256 = [string](Get-OptionalPropertyValue -Object $fixtureBaseline -Name "Sha256" -Default "")
      sizeBytes = [int64](Get-OptionalPropertyValue -Object $fixtureBaseline -Name "SizeBytes" -Default 0)
      rootElement = [string](Get-OptionalPropertyValue -Object $fixtureBaseline -Name "RootElement" -Default "")
    }
    $result.baseline = $expectedBaseline

    if (-not [string]::IsNullOrWhiteSpace($expectedBaseline.sha256) -and $expectedBaseline.sha256.ToLowerInvariant() -ne $profile.sha256) {
      $severity = if ([bool]$driftPolicy.FailOnBaselineMismatch) { "fail" } else { "warning" }
      $finding = [pscustomobject]@{
        severity = $severity
        code = "baseline-hash-mismatch"
        message = "Fixture hash does not match immutable baseline reference."
        expected = $expectedBaseline.sha256.ToLowerInvariant()
        actual = $profile.sha256
      }
      $result.findings += $finding
      if ($severity -eq "fail") {
        $result.status = "fail"
        $failures.Add([pscustomobject]@{ scenario = $scenario; code = $finding.code; message = $finding.message })
      }
      elseif ($result.status -eq "pass") {
        $result.status = "warning"
        $warnings.Add([pscustomobject]@{ scenario = $scenario; code = $finding.code; message = $finding.message })
      }
    }

    if ($expectedBaseline.sizeBytes -gt 0 -and $expectedBaseline.sizeBytes -ne $profile.sizeBytes) {
      $severity = if ([bool]$driftPolicy.FailOnBaselineMismatch) { "fail" } else { "warning" }
      $finding = [pscustomobject]@{
        severity = $severity
        code = "baseline-size-mismatch"
        message = "Fixture size does not match immutable baseline reference."
        expected = $expectedBaseline.sizeBytes
        actual = $profile.sizeBytes
      }
      $result.findings += $finding
      if ($severity -eq "fail") {
        $result.status = "fail"
        $failures.Add([pscustomobject]@{ scenario = $scenario; code = $finding.code; message = $finding.message })
      }
      elseif ($result.status -eq "pass") {
        $result.status = "warning"
        $warnings.Add([pscustomobject]@{ scenario = $scenario; code = $finding.code; message = $finding.message })
      }
    }

    if (-not [string]::IsNullOrWhiteSpace($expectedBaseline.rootElement) -and $expectedBaseline.rootElement -ne $profile.rootElement) {
      $severity = if ([bool]$driftPolicy.FailOnBaselineMismatch) { "fail" } else { "warning" }
      $finding = [pscustomobject]@{
        severity = $severity
        code = "baseline-root-mismatch"
        message = "Fixture XML root element does not match immutable baseline reference."
        expected = $expectedBaseline.rootElement
        actual = $profile.rootElement
      }
      $result.findings += $finding
      if ($severity -eq "fail") {
        $result.status = "fail"
        $failures.Add([pscustomobject]@{ scenario = $scenario; code = $finding.code; message = $finding.message })
      }
      elseif ($result.status -eq "pass") {
        $result.status = "warning"
        $warnings.Add([pscustomobject]@{ scenario = $scenario; code = $finding.code; message = $finding.message })
      }
    }
  }

  $entries.Add([pscustomobject]$result)
}

foreach ($entry in $entries) {
  $compareAgainstScenario = ""
  $expectedHashChange = $false
  $fixtureDefinition = $fixtures | Where-Object { [string]$_.Scenario -eq $entry.scenario } | Select-Object -First 1
  if ($null -ne $fixtureDefinition) {
    $compareAgainstScenario = [string](Get-OptionalPropertyValue -Object $fixtureDefinition -Name "CompareAgainstScenario" -Default "")
    $expectedHashChange = [bool](Get-OptionalPropertyValue -Object $fixtureDefinition -Name "ExpectedHashChange" -Default $false)
  }

  if ([string]::IsNullOrWhiteSpace($compareAgainstScenario)) {
    continue
  }

  if (-not $profilesByScenario.ContainsKey($compareAgainstScenario)) {
    $entry.status = "fail"
    $finding = [pscustomobject]@{
      severity = "fail"
      code = "missing-comparison-baseline"
      message = "Comparison baseline scenario '$compareAgainstScenario' was not available."
    }
    $entry.findings += $finding
    $failures.Add([pscustomobject]@{ scenario = $entry.scenario; code = $finding.code; message = $finding.message })
    continue
  }

  $currentProfile = $entry.profile
  $baselineProfile = $profilesByScenario[$compareAgainstScenario]

  $definitionThresholds = $null
  if ($null -ne $fixtureDefinition) {
    $definitionThresholds = Get-OptionalPropertyValue -Object $fixtureDefinition -Name "Thresholds"
  }

  $defaultThresholds = Get-OptionalPropertyValue -Object $driftPolicy -Name "DefaultThresholds"
  $defaultMaxSizeDeltaPercent = [double](Get-OptionalPropertyValue -Object $defaultThresholds -Name "MaxSizeDeltaPercent" -Default 0)
  $defaultMaxLineDeltaPercent = [double](Get-OptionalPropertyValue -Object $defaultThresholds -Name "MaxLineDeltaPercent" -Default 0)
  $definitionMaxSizeDeltaPercent = Get-OptionalPropertyValue -Object $definitionThresholds -Name "MaxSizeDeltaPercent"
  $definitionMaxLineDeltaPercent = Get-OptionalPropertyValue -Object $definitionThresholds -Name "MaxLineDeltaPercent"
  $maxSizeDeltaPercent = if ($null -ne $definitionMaxSizeDeltaPercent) { [double]$definitionMaxSizeDeltaPercent } else { $defaultMaxSizeDeltaPercent }
  $maxLineDeltaPercent = if ($null -ne $definitionMaxLineDeltaPercent) { [double]$definitionMaxLineDeltaPercent } else { $defaultMaxLineDeltaPercent }

  $sizeDeltaPercent = Get-PercentDelta -Baseline ([double]$baselineProfile.sizeBytes) -Current ([double]$currentProfile.sizeBytes)
  $lineDeltaPercent = Get-PercentDelta -Baseline ([double]$baselineProfile.lineCount) -Current ([double]$currentProfile.lineCount)
  $hashChanged = [bool]($currentProfile.sha256 -ne $baselineProfile.sha256)

  $entry.comparison = [pscustomobject]@{
    againstScenario = $compareAgainstScenario
    hashChanged = $hashChanged
    sizeDeltaPercent = $sizeDeltaPercent
    lineDeltaPercent = $lineDeltaPercent
    maxSizeDeltaPercent = $maxSizeDeltaPercent
    maxLineDeltaPercent = $maxLineDeltaPercent
  }

  if ($expectedHashChange -and -not $hashChanged) {
    $entry.status = "warning"
    $finding = [pscustomobject]@{
      severity = "warning"
      code = "unexpected-hash-stability"
      message = "Quarterly delta fixture did not change hash relative to baseline."
    }
    $entry.findings += $finding
    $warnings.Add([pscustomobject]@{ scenario = $entry.scenario; code = $finding.code; message = $finding.message })
  }

  $thresholdSeverity = [string]$driftPolicy.ThresholdBreachSeverity
  if ([string]::IsNullOrWhiteSpace($thresholdSeverity)) {
    $thresholdSeverity = "warning"
  }
  $thresholdSeverity = $thresholdSeverity.ToLowerInvariant()

  if ($sizeDeltaPercent -gt $maxSizeDeltaPercent) {
    $finding = [pscustomobject]@{
      severity = $thresholdSeverity
      code = "size-drift-threshold-exceeded"
      message = "Size drift exceeded threshold."
      actualPercent = $sizeDeltaPercent
      thresholdPercent = $maxSizeDeltaPercent
    }
    $entry.findings += $finding
    if ($thresholdSeverity -eq "fail") {
      $entry.status = "fail"
      $failures.Add([pscustomobject]@{ scenario = $entry.scenario; code = $finding.code; message = $finding.message })
    }
    elseif ($entry.status -eq "pass") {
      $entry.status = "warning"
      $warnings.Add([pscustomobject]@{ scenario = $entry.scenario; code = $finding.code; message = $finding.message })
    }
  }

  if ($lineDeltaPercent -gt $maxLineDeltaPercent) {
    $finding = [pscustomobject]@{
      severity = $thresholdSeverity
      code = "line-drift-threshold-exceeded"
      message = "Line-count drift exceeded threshold."
      actualPercent = $lineDeltaPercent
      thresholdPercent = $maxLineDeltaPercent
    }
    $entry.findings += $finding
    if ($thresholdSeverity -eq "fail") {
      $entry.status = "fail"
      $failures.Add([pscustomobject]@{ scenario = $entry.scenario; code = $finding.code; message = $finding.message })
    }
    elseif ($entry.status -eq "pass") {
      $entry.status = "warning"
      $warnings.Add([pscustomobject]@{ scenario = $entry.scenario; code = $finding.code; message = $finding.message })
    }
  }
}

$maxWarnings = [int]$driftPolicy.MaxWarnings
if ($warnings.Count -gt $maxWarnings) {
  $failures.Add([pscustomobject]@{
    scenario = "_pack"
    code = "warning-budget-exceeded"
    message = "Quarterly pack warnings ($($warnings.Count)) exceeded budget ($maxWarnings)."
  })
}

$overallPassed = ($failures.Count -eq 0)
$packDecision = if ($overallPassed) { "pass" } else { "fail" }

$entryArray = $entries.ToArray()
$warningArray = $warnings.ToArray()
$failureArray = $failures.ToArray()

$driftPath = Join-Path $outputFullPath "quarterly-pack-drift.json"
$summaryPath = Join-Path $outputFullPath "quarterly-pack-summary.json"
$reportPath = Join-Path $outputFullPath "quarterly-pack-report.md"

$driftPayload = [ordered]@{
  generatedAtUtc = (Get-Date).ToUniversalTime().ToString("o")
  packId = [string]$pack.PackId
  quarter = [string]$pack.Quarter
  schemaVersion = $schemaVersion
  repositoryRoot = [string]$RepositoryRoot
  packPath = $packFullPath
  entries = $entryArray
  warnings = $warningArray
  failures = $failureArray
}
$driftPayload | ConvertTo-Json -Depth 8 | Set-Content -Path $driftPath -Encoding UTF8

$summaryPayload = [ordered]@{
  generatedAtUtc = (Get-Date).ToUniversalTime().ToString("o")
  packId = [string]$pack.PackId
  quarter = [string]$pack.Quarter
  baselineLabel = [string]$pack.BaselineLabel
  schemaVersion = $schemaVersion
  outputRoot = $outputFullPath
  repositoryRoot = [string]$RepositoryRoot
  overallPassed = [bool]$overallPassed
  decision = $packDecision
  fixtures = [ordered]@{
    total = $entryArray.Count
    passed = @($entryArray | Where-Object { $_.status -eq "pass" }).Count
    warnings = @($entryArray | Where-Object { $_.status -eq "warning" }).Count
    failed = @($entryArray | Where-Object { $_.status -eq "fail" }).Count
  }
  policy = [ordered]@{
    maxWarnings = $maxWarnings
    warningCount = $warningArray.Count
    failOnMissingFixture = [bool]$driftPolicy.FailOnMissingFixture
    failOnBaselineMismatch = [bool]$driftPolicy.FailOnBaselineMismatch
    thresholdBreachSeverity = [string]$driftPolicy.ThresholdBreachSeverity
  }
  artifacts = [ordered]@{
    driftJson = $driftPath
    report = $reportPath
  }
}
$summaryPayload | ConvertTo-Json -Depth 8 | Set-Content -Path $summaryPath -Encoding UTF8

$report = New-Object System.Text.StringBuilder
[void]$report.AppendLine("# Quarterly Compatibility Regression Pack")
[void]$report.AppendLine()
[void]$report.AppendLine("- Pack ID: $($pack.PackId)")
[void]$report.AppendLine("- Quarter: $($pack.Quarter)")
[void]$report.AppendLine("- Baseline: $($pack.BaselineLabel)")
[void]$report.AppendLine("- Decision: $packDecision")
[void]$report.AppendLine("- Fixture totals: pass=$($summaryPayload.fixtures.passed), warning=$($summaryPayload.fixtures.warnings), fail=$($summaryPayload.fixtures.failed)")
[void]$report.AppendLine("- Warning budget: $($warnings.Count)/$maxWarnings")
[void]$report.AppendLine()
[void]$report.AppendLine("## Fixture Results")
[void]$report.AppendLine()
[void]$report.AppendLine("| Scenario | Status | Hash | Size (bytes) | Lines | Drift |")
[void]$report.AppendLine("|----------|--------|------|--------------|-------|-------|")

foreach ($entry in ($entries | Sort-Object scenario)) {
  $profile = $entry.profile
  $hash = if ($null -ne $profile) { [string]$profile.sha256 } else { "n/a" }
  $size = if ($null -ne $profile) { [string]$profile.sizeBytes } else { "n/a" }
  $lines = if ($null -ne $profile) { [string]$profile.lineCount } else { "n/a" }
  $driftText = "n/a"
  if ($null -ne $entry.comparison) {
    $driftText = "size=$($entry.comparison.sizeDeltaPercent)% line=$($entry.comparison.lineDeltaPercent)%"
  }
  [void]$report.AppendLine("| $($entry.scenario) | $($entry.status.ToUpperInvariant()) | $hash | $size | $lines | $driftText |")
}

if ($warnings.Count -gt 0 -or $failures.Count -gt 0) {
  [void]$report.AppendLine()
  [void]$report.AppendLine("## Findings")
  [void]$report.AppendLine()
  [void]$report.AppendLine("| Severity | Scenario | Code | Message |")
  [void]$report.AppendLine("|----------|----------|------|---------|")

  foreach ($entry in ($entries | Sort-Object scenario)) {
    foreach ($finding in @($entry.findings)) {
      [void]$report.AppendLine("| $($finding.severity) | $($entry.scenario) | $($finding.code) | $($finding.message) |")
    }
  }

  foreach ($failure in ($failures | Where-Object { $_.scenario -eq "_pack" })) {
    [void]$report.AppendLine("| fail | _pack | $($failure.code) | $($failure.message) |")
  }
}

Set-Content -Path $reportPath -Value $report.ToString() -Encoding UTF8

Write-Host "[quarterly-pack] manifest: $packFullPath" -ForegroundColor Cyan
Write-Host "[quarterly-pack] output:   $outputFullPath" -ForegroundColor Cyan
Write-Host "[quarterly-pack] summary:  $summaryPath" -ForegroundColor Green
Write-Host "[quarterly-pack] drift:    $driftPath" -ForegroundColor Green
Write-Host "[quarterly-pack] report:   $reportPath" -ForegroundColor Green

if (-not $overallPassed) {
  Write-Host "[quarterly-pack] Quarterly compatibility regression pack failed policy checks." -ForegroundColor Red
  exit 1
}

Write-Host "[quarterly-pack] Quarterly compatibility regression pack passed." -ForegroundColor Green
exit 0
