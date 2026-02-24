param(
  [string]$SourceDirectory = ".artifacts/coverage",
  [string]$OutputPath = ".artifacts/coverage/ci/coverage.cobertura.xml"
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

function Get-IntAttributeOrNull {
  param(
    [Parameter(Mandatory = $true)]$Element,
    [Parameter(Mandatory = $true)][string]$AttributeName
  )

  $rawValue = [string]$Element.GetAttribute($AttributeName)
  if ([string]::IsNullOrWhiteSpace($rawValue)) {
    return $null
  }

  $parsed = 0
  if (-not [int]::TryParse($rawValue, [ref]$parsed)) {
    throw "Cobertura attribute '$AttributeName' must be an integer, found '$rawValue'."
  }

  return $parsed
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

function Get-PackageLineMetrics {
  param(
    [Parameter(Mandatory = $true)]$PackageElement
  )

  $lineNodes = @($PackageElement.SelectNodes(".//line"))
  if ($lineNodes.Count -eq 0) {
    return [ordered]@{
      linesCovered = 0
      linesValid = 0
      branchesCovered = 0
      branchesValid = 0
    }
  }

  $linesCovered = 0
  $linesValid = 0
  $branchesCovered = 0
  $branchesValid = 0

  foreach ($lineNode in $lineNodes) {
    $lineHitsRaw = [string]$lineNode.GetAttribute("hits")
    if ([string]::IsNullOrWhiteSpace($lineHitsRaw)) {
      continue
    }

    $lineHits = 0
    if (-not [int]::TryParse($lineHitsRaw, [ref]$lineHits)) {
      continue
    }

    $linesValid += 1
    if ($lineHits -gt 0) {
      $linesCovered += 1
    }

    if ($lineNode.GetAttribute("branch").Equals("true", [StringComparison]::OrdinalIgnoreCase)) {
      $conditionCoverage = [string]$lineNode.GetAttribute("condition-coverage")
      if ([string]::IsNullOrWhiteSpace($conditionCoverage)) {
        continue
      }

      if ($conditionCoverage -match '^\s*\d+(?:\.\d+)?%\s*\(\s*(\d+)\s*/\s*(\d+)\s*\)\s*$') {
        $coveredBranches = [int]$matches[1]
        $validBranches = [int]$matches[2]
        if ($validBranches -gt 0) {
          $branchesCovered += $coveredBranches
          $branchesValid += $validBranches
        }
      }
    }
  }

  return [ordered]@{
    linesCovered = $linesCovered
    linesValid = $linesValid
    branchesCovered = $branchesCovered
    branchesValid = $branchesValid
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
  $repositoryRoot = (Resolve-Path (Join-Path $scriptRoot "..\..")).Path
  $sourceDirectoryFull = Resolve-FromRepositoryRoot -RepositoryRoot $repositoryRoot -Candidate $SourceDirectory
  $outputPathFull = Resolve-FromRepositoryRoot -RepositoryRoot $repositoryRoot -Candidate $OutputPath
  $outputDirectoryFull = Split-Path -Parent $outputPathFull

  $coverageFiles = @(Get-ChildItem -Path $sourceDirectoryFull -Recurse -Filter 'coverage.cobertura.xml' -File)
  if ($coverageFiles.Count -eq 0) {
    throw "No coverage files found under $sourceDirectoryFull"
  }

  $mergedPackages = @{ }
  $totalLinesCovered = 0
  $totalLinesValid = 0
  $totalBranchesCovered = 0
  $totalBranchesValid = 0
  $maxTimestamp = 0
  $maxVersion = ""

  foreach ($coverageFile in ($coverageFiles | Sort-Object -Property FullName)) {
    [xml]$coverageXml = Get-Content -LiteralPath $coverageFile.FullName -Raw

    $coverageRoot = $coverageXml.coverage
    if ($null -eq $coverageRoot) {
      throw "Coverage file does not contain a <coverage> root node: $($coverageFile.FullName)"
    }

    $rootLinesCovered = Get-IntAttribute -Element $coverageRoot -AttributeName "lines-covered"
    $rootLinesValid = Get-IntAttribute -Element $coverageRoot -AttributeName "lines-valid"
    $rootBranchesCovered = Get-IntAttribute -Element $coverageRoot -AttributeName "branches-covered"
    $rootBranchesValid = Get-IntAttribute -Element $coverageRoot -AttributeName "branches-valid"

    $totalLinesCovered += $rootLinesCovered
    $totalLinesValid += $rootLinesValid
    $totalBranchesCovered += $rootBranchesCovered
    $totalBranchesValid += $rootBranchesValid

    $timestampRaw = $coverageRoot.GetAttribute("timestamp")
    if (-not [string]::IsNullOrWhiteSpace($timestampRaw)) {
      $timestampValue = 0
      if (-not [int]::TryParse($timestampRaw, [ref]$timestampValue)) {
        throw "Invalid coverage timestamp '$timestampRaw' in $($coverageFile.FullName)."
      }
      if ($timestampValue -gt $maxTimestamp) {
        $maxTimestamp = $timestampValue
      }
    }

    $version = [string]$coverageRoot.GetAttribute("version")
    if (-not [string]::IsNullOrWhiteSpace($version)) {
      $maxVersion = $version
    }

    $packageNodes = @($coverageRoot.SelectNodes("/coverage/packages/package"))
    if ($packageNodes.Count -eq 0) {
      throw "Coverage file does not contain valid <packages>/<package> entries: $($coverageFile.FullName)"
    }

    foreach ($package in $packageNodes) {
      $packageName = [string]$package.GetAttribute("name")
      if ([string]::IsNullOrWhiteSpace($packageName)) {
        throw "Coverage package entry is missing 'name' in $($coverageFile.FullName)"
      }

      $linesCovered = Get-IntAttributeOrNull -Element $package -AttributeName "lines-covered"
      $linesValid = Get-IntAttributeOrNull -Element $package -AttributeName "lines-valid"
      $branchesCovered = Get-IntAttributeOrNull -Element $package -AttributeName "branches-covered"
      $branchesValid = Get-IntAttributeOrNull -Element $package -AttributeName "branches-valid"

      if ($null -in @($linesCovered, $linesValid, $branchesCovered, $branchesValid)) {
        $metrics = Get-PackageLineMetrics -PackageElement $package
        $linesCovered = $metrics.linesCovered
        $linesValid = $metrics.linesValid
        $branchesCovered = $metrics.branchesCovered
        $branchesValid = $metrics.branchesValid
      }

      if (-not $mergedPackages.ContainsKey($packageName)) {
        $mergedPackages[$packageName] = [ordered]@{
          linesCovered = 0
          linesValid = 0
          branchesCovered = 0
          branchesValid = 0
        }
      }

      $entry = $mergedPackages[$packageName]
      $entry.linesCovered += $linesCovered
      $entry.linesValid += $linesValid
      $entry.branchesCovered += $branchesCovered
      $entry.branchesValid += $branchesValid
    }
  }

  if ($mergedPackages.Count -eq 0) {
    throw "No package entries found in coverage files under $sourceDirectoryFull"
  }

  $mergedXml = New-Object System.Text.StringBuilder
  [void]$mergedXml.AppendLine('<?xml version="1.0" encoding="utf-8"?>')
  $coverageElement = '<coverage lines-covered="{0}" lines-valid="{1}" branches-covered="{2}" branches-valid="{3}" timestamp="{4}" version="{5}">' -f @($totalLinesCovered, $totalLinesValid, $totalBranchesCovered, $totalBranchesValid, $maxTimestamp, $maxVersion)
  [void]$mergedXml.AppendLine($coverageElement)
  [void]$mergedXml.AppendLine('  <packages>')

foreach ($packageName in ($mergedPackages.Keys | Sort-Object)) {
  $entry = $mergedPackages[$packageName]
  $packageLine = '    <package name="{0}" lines-covered="{1}" lines-valid="{2}" branches-covered="{3}" branches-valid="{4}" />' -f @($packageName, $entry.linesCovered, $entry.linesValid, $entry.branchesCovered, $entry.branchesValid)
  [void]$mergedXml.AppendLine($packageLine)
}

  [void]$mergedXml.AppendLine('  </packages>')
  [void]$mergedXml.AppendLine('</coverage>')

  [void](New-Item -ItemType Directory -Path $outputDirectoryFull -Force)
  Write-Utf8NoBom -Path $outputPathFull -Content $mergedXml.ToString()

  Write-Host "[coverage-merge] Wrote merged coverage report to $outputPathFull"
  exit 0
}
catch {
  Write-Host "[coverage-merge] Failed: $($_.Exception.Message)" -ForegroundColor Red
  exit 1
}
