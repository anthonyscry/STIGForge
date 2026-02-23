param(
  [string]$CoverageReportPath = ".artifacts/coverage/coverage.cobertura.xml",
  [string]$OutputDirectory = ".artifacts/coverage"
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

function Get-IntAttributeOrZero {
  param(
    [Parameter(Mandatory = $true)]$Element,
    [Parameter(Mandatory = $true)][string]$AttributeName
  )

  $rawValue = [string]$Element.GetAttribute($AttributeName)
  if ([string]::IsNullOrWhiteSpace($rawValue)) {
    return 0
  }

  $parsed = 0
  if (-not [int]::TryParse($rawValue, [ref]$parsed)) {
    throw "Cobertura attribute '$AttributeName' must be an integer, found '$rawValue'."
  }

  return $parsed
}

function Get-CoveragePercent {
  param(
    [int]$Covered,
    [int]$Valid
  )

  if ($Valid -le 0) {
    return 0.0
  }

  return [math]::Round(($Covered / $Valid) * 100, 2)
}

function New-PackageSummary {
  param(
    [Parameter(Mandatory = $true)]$PackageElement
  )

  $linesCovered = Get-IntAttributeOrZero -Element $PackageElement -AttributeName "lines-covered"
  $linesValid = Get-IntAttributeOrZero -Element $PackageElement -AttributeName "lines-valid"
  $branchesCovered = Get-IntAttributeOrZero -Element $PackageElement -AttributeName "branches-covered"
  $branchesValid = Get-IntAttributeOrZero -Element $PackageElement -AttributeName "branches-valid"

  return [ordered]@{
    name = [string]$PackageElement.name
    linesCovered = $linesCovered
    linesValid = $linesValid
    lineCoveragePercent = Get-CoveragePercent -Covered $linesCovered -Valid $linesValid
    branchesCovered = $branchesCovered
    branchesValid = $branchesValid
    branchCoveragePercent = Get-CoveragePercent -Covered $branchesCovered -Valid $branchesValid
  }
}

function Write-Utf8NoBom {
  param(
    [Parameter(Mandatory = $true)][string]$Path,
    [Parameter(Mandatory = $true)][string]$Content
  )

  $encoding = [System.Text.UTF8Encoding]::new($false)
  [IO.File]::WriteAllText($Path, $Content, $encoding)
}

try {
  $scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
  $repositoryRoot = (Resolve-Path (Join-Path $scriptRoot "../..")).Path
  $coveragePathFull = Resolve-FromRepositoryRoot -RepositoryRoot $repositoryRoot -Candidate $CoverageReportPath
  $outputDirectoryFull = Resolve-FromRepositoryRoot -RepositoryRoot $repositoryRoot -Candidate $OutputDirectory

  if (-not (Test-Path -LiteralPath $coveragePathFull)) {
    throw "Coverage report file not found: $coveragePathFull"
  }

  [xml]$coverageXml = Get-Content -LiteralPath $coveragePathFull -Raw
  $packageNodes = @($coverageXml.coverage.packages.package)
  if ($packageNodes.Count -eq 0) {
    throw "Cobertura report does not contain package coverage entries."
  }

  $packageSummaries = @($packageNodes | ForEach-Object { New-PackageSummary -PackageElement $_ } | Sort-Object -Property name)

  $totalLinesCovered = Get-IntAttributeOrZero -Element $coverageXml.coverage -AttributeName "lines-covered"
  $totalLinesValid = Get-IntAttributeOrZero -Element $coverageXml.coverage -AttributeName "lines-valid"
  $totalBranchesCovered = Get-IntAttributeOrZero -Element $coverageXml.coverage -AttributeName "branches-covered"
  $totalBranchesValid = Get-IntAttributeOrZero -Element $coverageXml.coverage -AttributeName "branches-valid"

  $summaryObject = [ordered]@{
    totals = [ordered]@{
      linesCovered = $totalLinesCovered
      linesValid = $totalLinesValid
      lineCoveragePercent = Get-CoveragePercent -Covered $totalLinesCovered -Valid $totalLinesValid
      branchesCovered = $totalBranchesCovered
      branchesValid = $totalBranchesValid
      branchCoveragePercent = Get-CoveragePercent -Covered $totalBranchesCovered -Valid $totalBranchesValid
    }
    packages = $packageSummaries
  }

  [void](New-Item -ItemType Directory -Path $outputDirectoryFull -Force)

  $summaryPath = Join-Path $outputDirectoryFull "coverage-summary.json"
  $markdownPath = Join-Path $outputDirectoryFull "coverage-report.md"

  $summaryJson = $summaryObject | ConvertTo-Json -Depth 5
  Write-Utf8NoBom -Path $summaryPath -Content $summaryJson

  $linePercent = [string]::Format([Globalization.CultureInfo]::InvariantCulture, "{0:F2}", $summaryObject.totals.lineCoveragePercent)
  $branchPercent = [string]::Format([Globalization.CultureInfo]::InvariantCulture, "{0:F2}", $summaryObject.totals.branchCoveragePercent)

  $markdownLines = New-Object System.Collections.Generic.List[string]
  [void]$markdownLines.Add("# Coverage Report")
  [void]$markdownLines.Add("")
  [void]$markdownLines.Add("## Totals")
  [void]$markdownLines.Add("")
  [void]$markdownLines.Add("- Line Coverage: $linePercent% ($($summaryObject.totals.linesCovered)/$($summaryObject.totals.linesValid))")
  [void]$markdownLines.Add("- Branch Coverage: $branchPercent% ($($summaryObject.totals.branchesCovered)/$($summaryObject.totals.branchesValid))")
  [void]$markdownLines.Add("")
  [void]$markdownLines.Add("## Packages")
  [void]$markdownLines.Add("")
  [void]$markdownLines.Add("| Package | Line Coverage | Branch Coverage |")
  [void]$markdownLines.Add("| --- | ---: | ---: |")

  foreach ($package in $packageSummaries) {
    $pkgLinePercent = [string]::Format([Globalization.CultureInfo]::InvariantCulture, "{0:F2}", $package.lineCoveragePercent)
    $pkgBranchPercent = [string]::Format([Globalization.CultureInfo]::InvariantCulture, "{0:F2}", $package.branchCoveragePercent)
    [void]$markdownLines.Add("| $($package.name) | $pkgLinePercent% | $pkgBranchPercent% |")
  }

  Write-Utf8NoBom -Path $markdownPath -Content ($markdownLines -join "`n")

  Write-Host "[coverage-report] Wrote $summaryPath"
  Write-Host "[coverage-report] Wrote $markdownPath"
  exit 0
}
catch {
  Write-Host "[coverage-report] Failed: $($_.Exception.Message)" -ForegroundColor Red
  exit 1
}
