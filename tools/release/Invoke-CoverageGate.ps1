param(
  [string]$CoverageReportPath = "",
  [string]$PolicyPath = ""
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

function Get-IntAttribute {
  param(
    [Parameter(Mandatory = $true)]$Element,
    [Parameter(Mandatory = $true)][string]$AttributeName
  )

  $rawValue = [string]$Element.GetAttribute($AttributeName)
  if ([string]::IsNullOrWhiteSpace($rawValue)) {
    throw "Missing required Cobertura attribute '$AttributeName'."
  }

  $parsed = 0
  if (-not [int]::TryParse($rawValue, [ref]$parsed)) {
    throw "Cobertura attribute '$AttributeName' must be an integer, found '$rawValue'."
  }

  return $parsed
}

function Invoke-CoverageGate {
  param(
    [Parameter(Mandatory = $true)][string]$CoveragePath,
    [Parameter(Mandatory = $true)][string]$CoveragePolicyPath
  )

  if (-not (Test-Path -LiteralPath $CoveragePolicyPath)) {
    throw "Coverage policy file not found: $CoveragePolicyPath"
  }

  if (-not (Test-Path -LiteralPath $CoveragePath)) {
    throw "Coverage report file not found: $CoveragePath"
  }

  $policy = Get-Content -Path $CoveragePolicyPath -Raw | ConvertFrom-Json
  $thresholdRaw = $policy.minimumLineCoveragePercent
  if ($null -eq $thresholdRaw -or [string]::IsNullOrWhiteSpace([string]$thresholdRaw)) {
    throw "Coverage policy minimumLineCoveragePercent is required."
  }

  $threshold = 0.0
  if (-not [double]::TryParse([string]$thresholdRaw, [Globalization.NumberStyles]::Float, [Globalization.CultureInfo]::InvariantCulture, [ref]$threshold)) {
    throw "Coverage policy minimumLineCoveragePercent must be numeric, found '$thresholdRaw'."
  }

  if ($threshold -lt 0 -or $threshold -gt 100) {
    throw "Coverage policy minimumLineCoveragePercent must be between 0 and 100 (inclusive), found '$thresholdRaw'."
  }

  $criticalAssemblies = @($policy.criticalAssemblies | ForEach-Object { [string]$_ })

  if ($criticalAssemblies.Count -eq 0) {
    throw "Coverage policy must include at least one critical assembly."
  }

  [xml]$coverageXml = Get-Content -Path $CoveragePath -Raw
  $allPackages = @($coverageXml.coverage.packages.package)
  if ($allPackages.Count -eq 0) {
    throw "Cobertura report does not contain package coverage entries."
  }

  $criticalLookup = New-Object System.Collections.Generic.HashSet[string]([StringComparer]::OrdinalIgnoreCase)
  foreach ($assembly in $criticalAssemblies) {
    [void]$criticalLookup.Add($assembly)
  }

  $availablePackageLookup = New-Object System.Collections.Generic.HashSet[string]([StringComparer]::OrdinalIgnoreCase)
  foreach ($package in $allPackages) {
    [void]$availablePackageLookup.Add([string]$package.name)
  }

  $missingCriticalAssemblies = New-Object 'System.Collections.Generic.List[string]'
  $missingSeenLookup = New-Object System.Collections.Generic.HashSet[string]([StringComparer]::OrdinalIgnoreCase)
  foreach ($assembly in $criticalAssemblies) {
    if (-not $availablePackageLookup.Contains($assembly) -and $missingSeenLookup.Add($assembly)) {
      [void]$missingCriticalAssemblies.Add($assembly)
    }
  }

  if ($missingCriticalAssemblies.Count -gt 0) {
    $missingCriticalAssemblies.Sort([StringComparer]::OrdinalIgnoreCase)
    throw "Cobertura report is missing policy critical assemblies: $($missingCriticalAssemblies -join ', ')"
  }

  $scopedPackages = @($allPackages | Where-Object { $criticalLookup.Contains([string]$_.name) })
  if ($scopedPackages.Count -eq 0) {
    throw "Cobertura report did not contain any policy critical assemblies: $($criticalAssemblies -join ', ')"
  }

  $scopedLinesCovered = 0
  $scopedLinesValid = 0
  foreach ($package in $scopedPackages) {
    $scopedLinesCovered += Get-IntAttribute -Element $package -AttributeName "lines-covered"
    $scopedLinesValid += Get-IntAttribute -Element $package -AttributeName "lines-valid"
  }

  if ($scopedLinesValid -le 0) {
    throw "Scoped coverage has zero valid lines; cannot evaluate gate."
  }

  $actualPercentRaw = ($scopedLinesCovered / $scopedLinesValid) * 100
  $actualPercentRounded = [math]::Round($actualPercentRaw, 2)
  $diagnostic = "threshold=$threshold actual=$actualPercentRounded scopedLinesCovered=$scopedLinesCovered scopedLinesValid=$scopedLinesValid"

  if ($actualPercentRaw -lt $threshold) {
    Write-Host "[coverage-gate] Coverage gate failed: $diagnostic" -ForegroundColor Red
    return 1
  }

  Write-Host "[coverage-gate] Coverage gate passed: $diagnostic" -ForegroundColor Green
  return 0
}

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repositoryRoot = (Resolve-Path (Join-Path $scriptRoot "..\..")).Path

if ([string]::IsNullOrWhiteSpace($PolicyPath)) {
  $PolicyPath = Join-Path $scriptRoot "coverage-gate-policy.json"
}

if ([string]::IsNullOrWhiteSpace($CoverageReportPath)) {
  $CoverageReportPath = ".artifacts\coverage\coverage.cobertura.xml"
}

$policyPathFull = Resolve-FromRepositoryRoot -RepositoryRoot $repositoryRoot -Candidate $PolicyPath
$coveragePathFull = Resolve-FromRepositoryRoot -RepositoryRoot $repositoryRoot -Candidate $CoverageReportPath

try {
  $exitCode = Invoke-CoverageGate -CoveragePath $coveragePathFull -CoveragePolicyPath $policyPathFull
  exit $exitCode
}
catch {
  Write-Host "[coverage-gate] Coverage gate failed: $($_.Exception.Message)" -ForegroundColor Red
  exit 1
}
