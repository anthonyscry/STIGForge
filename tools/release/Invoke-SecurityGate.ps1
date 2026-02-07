param(
  [string]$OutputRoot = "",
  [string]$RepositoryRoot = "",
  [string]$VulnerabilityExceptionsPath = "",
  [string]$LicensePolicyPath = "",
  [string]$SecretsPolicyPath = "",
  [switch]$SkipLicenseLookup,
  [switch]$SkipSecrets
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Convert-ToRelativePath {
  param(
    [Parameter(Mandatory = $true)][string]$BasePath,
    [Parameter(Mandatory = $true)][string]$TargetPath
  )

  $baseUri = [System.Uri]((Resolve-Path $BasePath).Path + [IO.Path]::DirectorySeparatorChar)
  $targetUri = [System.Uri]((Resolve-Path $TargetPath).Path)
  return [System.Uri]::UnescapeDataString($baseUri.MakeRelativeUri($targetUri).ToString().Replace('/', [IO.Path]::DirectorySeparatorChar))
}

function Invoke-DotnetJson {
  param(
    [Parameter(Mandatory = $true)][string]$Name,
    [Parameter(Mandatory = $true)][string[]]$Arguments,
    [Parameter(Mandatory = $true)][string]$LogPath,
    [Parameter(Mandatory = $true)][string]$JsonPath
  )

  Write-Host "[security-gate] $Name" -ForegroundColor Cyan
  Write-Host "[security-gate]   dotnet $($Arguments -join ' ')"

  $output = & dotnet @Arguments 2>&1
  $exitCode = $LASTEXITCODE
  $output | Out-File -FilePath $LogPath -Encoding UTF8

  if ($exitCode -ne 0) {
    throw "Step '$Name' failed with exit code $exitCode. See $LogPath"
  }

  $text = ($output -join [Environment]::NewLine)
  $jsonStart = $text.IndexOf('{')
  if ($jsonStart -lt 0) {
    throw "Step '$Name' did not return JSON output. See $LogPath"
  }

  $jsonText = $text.Substring($jsonStart)
  Set-Content -Path $JsonPath -Value $jsonText -Encoding UTF8
  return ($jsonText | ConvertFrom-Json)
}

function Get-PackageRowsFromDotnetList {
  param(
    [Parameter(Mandatory = $true)]$DotnetListJson,
    [switch]$IncludeTransitive
  )

  $rows = @()
  foreach ($project in @($DotnetListJson.projects)) {
    $frameworks = @()
    if ($project.PSObject.Properties.Name -contains "frameworks") {
      $frameworks = @($project.frameworks)
    }

    foreach ($framework in $frameworks) {
      $topLevelPackages = @()
      if ($framework.PSObject.Properties.Name -contains "topLevelPackages") {
        $topLevelPackages = @($framework.topLevelPackages)
      }

      foreach ($pkg in $topLevelPackages) {
        $vulnerabilities = @()
        if ($pkg.PSObject.Properties.Name -contains "vulnerabilities") {
          $vulnerabilities = @($pkg.vulnerabilities)
        }

        $rows += [pscustomobject]@{
          project = $project.path
          framework = $framework.framework
          packageId = $pkg.id
          version = $pkg.resolvedVersion
          vulnerabilities = $vulnerabilities
          transitive = $false
        }
      }

      if ($IncludeTransitive) {
        $transitivePackages = @()
        if ($framework.PSObject.Properties.Name -contains "transitivePackages") {
          $transitivePackages = @($framework.transitivePackages)
        }

        foreach ($pkg in $transitivePackages) {
          $vulnerabilities = @()
          if ($pkg.PSObject.Properties.Name -contains "vulnerabilities") {
            $vulnerabilities = @($pkg.vulnerabilities)
          }

          $rows += [pscustomobject]@{
            project = $project.path
            framework = $framework.framework
            packageId = $pkg.id
            version = $pkg.resolvedVersion
            vulnerabilities = $vulnerabilities
            transitive = $true
          }
        }
      }
    }
  }

  return $rows
}

function Test-LicenseExpressionAllowed {
  param(
    [Parameter(Mandatory = $true)][string]$Expression,
    [Parameter(Mandatory = $true)][System.Collections.Generic.HashSet[string]]$AllowedLicenses
  )

  if ([string]::IsNullOrWhiteSpace($Expression)) { return $false }

  $tokens = [regex]::Matches($Expression, "[A-Za-z0-9\.-]+") | ForEach-Object { $_.Value.ToUpperInvariant() }
  $filtered = @($tokens | Where-Object { $_ -ne "AND" -and $_ -ne "OR" -and $_ -ne "WITH" })
  if ($filtered.Count -eq 0) { return $false }

  foreach ($token in $filtered) {
    if (-not $AllowedLicenses.Contains($token)) { return $false }
  }

  return $true
}

function Get-NuGetLicenseInfo {
  param(
    [Parameter(Mandatory = $true)][string]$PackageId,
    [Parameter(Mandatory = $true)][string]$Version
  )

  $idLower = $PackageId.ToLowerInvariant()
  $versionLower = $Version.ToLowerInvariant()
  $url = "https://api.nuget.org/v3-flatcontainer/$idLower/$versionLower/$idLower.nuspec"

  try {
    [xml]$xml = Invoke-RestMethod -Uri $url -Method Get -TimeoutSec 30
    $metadata = $xml.package.metadata

    $licenseExpression = ""
    $licenseUrl = ""

    if ($metadata.license) {
      $licenseType = [string]$metadata.license.type
      $licenseText = [string]$metadata.license.'#text'
      if ($licenseType -eq "expression") {
        $licenseExpression = $licenseText
      }
      elseif ($licenseType -eq "file") {
        $licenseExpression = "FILE:" + $licenseText
      }
    }

    if ($metadata.licenseUrl) {
      $licenseUrl = [string]$metadata.licenseUrl
    }

    return [pscustomobject]@{
      packageId = $PackageId
      version = $Version
      sourceUrl = $url
      licenseExpression = $licenseExpression
      licenseUrl = $licenseUrl
      error = ""
    }
  }
  catch {
    return [pscustomobject]@{
      packageId = $PackageId
      version = $Version
      sourceUrl = $url
      licenseExpression = ""
      licenseUrl = ""
      error = $_.Exception.Message
    }
  }
}

function Test-IsPathIgnored {
  param(
    [Parameter(Mandatory = $true)][string]$RelativePath,
    [Parameter(Mandatory = $true)][string[]]$IgnoredGlobs
  )

  $normalized = $RelativePath.Replace('\', '/')
  foreach ($glob in $IgnoredGlobs) {
    $pattern = $glob.Replace('\', '/').Replace('**', '*')
    if ($normalized -like $pattern) {
      return $true
    }
  }

  return $false
}

function Find-SecretFindings {
  param(
    [Parameter(Mandatory = $true)][string]$RepoRoot,
    [Parameter(Mandatory = $true)]$SecretsPolicy
  )

  $excludedDirectories = @($SecretsPolicy.excludedDirectories | ForEach-Object { ([string]$_).ToLowerInvariant() })
  $includedExtensions = New-Object System.Collections.Generic.HashSet[string]([StringComparer]::OrdinalIgnoreCase)
  foreach ($ext in @($SecretsPolicy.includedExtensions)) { [void]$includedExtensions.Add([string]$ext) }

  $ignoredGlobs = @($SecretsPolicy.ignoredFileGlobs | ForEach-Object { [string]$_ })
  $ignoredLinePatterns = @($SecretsPolicy.ignoredLinePatterns | ForEach-Object { [string]$_ })

  $rules = @(
    @{ id = "private-key"; regex = '-----BEGIN (?:RSA|EC|DSA|OPENSSH|PGP) PRIVATE KEY-----' },
    @{ id = "aws-access-key"; regex = 'AKIA[0-9A-Z]{16}' },
    @{ id = "generic-secret-assignment"; regex = '(?i)(api[_-]?key|secret|token|password)\s*[:=]\s*["''][^"'']{10,}["'']' },
    @{ id = "connection-string-secret"; regex = '(?i)(connectionstring|data source|server)\s*[:=].*(password|pwd)\s*=' }
  )

  $findings = @()

  foreach ($file in Get-ChildItem -Path $RepoRoot -Recurse -File) {
    $relative = Convert-ToRelativePath -BasePath $RepoRoot -TargetPath $file.FullName
    $segments = @($relative -split '[\\/]')
    $segmentMatch = $false
    foreach ($segment in $segments) {
      if ($excludedDirectories -contains $segment.ToLowerInvariant()) {
        $segmentMatch = $true
        break
      }
    }
    if ($segmentMatch) { continue }

    if (Test-IsPathIgnored -RelativePath $relative -IgnoredGlobs $ignoredGlobs) { continue }

    $extension = [IO.Path]::GetExtension($file.Name)
    if (-not $includedExtensions.Contains($extension)) { continue }

    $lines = Get-Content -Path $file.FullName -ErrorAction SilentlyContinue
    if ($null -eq $lines) { continue }

    $lineCollection = @($lines)

    for ($lineNumber = 0; $lineNumber -lt $lineCollection.Count; $lineNumber++) {
      $lineText = [string]$lineCollection[$lineNumber]
      $ignoredLine = $false
      foreach ($ignored in $ignoredLinePatterns) {
        if ([string]::IsNullOrWhiteSpace($ignored)) { continue }
        if ($lineText -match [regex]::Escape($ignored)) {
          $ignoredLine = $true
          break
        }
      }
      if ($ignoredLine) { continue }

      foreach ($rule in $rules) {
        if ([string]::IsNullOrWhiteSpace([string]$rule.regex)) { continue }
        if ([regex]::IsMatch($lineText, $rule.regex)) {
          $findings += [pscustomobject]@{
            rule = $rule.id
            file = $relative
            line = $lineNumber + 1
            excerpt = if ($lineText.Length -gt 180) { $lineText.Substring(0, 180) + "..." } else { $lineText }
          }
        }
      }
    }
  }

  return $findings
}

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) {
  $RepositoryRoot = Resolve-Path (Join-Path $scriptRoot "..\..")
}
else {
  $RepositoryRoot = Resolve-Path $RepositoryRoot
}

if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
  $stamp = Get-Date -Format "yyyyMMdd-HHmmss"
  $OutputRoot = Join-Path $RepositoryRoot ".artifacts\security-gate\$stamp"
}

if ([string]::IsNullOrWhiteSpace($VulnerabilityExceptionsPath)) {
  $VulnerabilityExceptionsPath = Join-Path $scriptRoot "security-vulnerability-exceptions.json"
}
if ([string]::IsNullOrWhiteSpace($LicensePolicyPath)) {
  $LicensePolicyPath = Join-Path $scriptRoot "security-license-policy.json"
}
if ([string]::IsNullOrWhiteSpace($SecretsPolicyPath)) {
  $SecretsPolicyPath = Join-Path $scriptRoot "security-secrets-policy.json"
}

$outputRootFull = [IO.Path]::GetFullPath($OutputRoot)
$logsRoot = Join-Path $outputRootFull "logs"
$reportsRoot = Join-Path $outputRootFull "reports"

New-Item -ItemType Directory -Path $outputRootFull -Force | Out-Null
New-Item -ItemType Directory -Path $logsRoot -Force | Out-Null
New-Item -ItemType Directory -Path $reportsRoot -Force | Out-Null

Write-Host "[security-gate] repository: $RepositoryRoot"
Write-Host "[security-gate] output:     $outputRootFull"

Push-Location $RepositoryRoot

try {
  $vulnPolicy = Get-Content -Path $VulnerabilityExceptionsPath -Raw | ConvertFrom-Json
  $licensePolicy = Get-Content -Path $LicensePolicyPath -Raw | ConvertFrom-Json
  $secretsPolicy = Get-Content -Path $SecretsPolicyPath -Raw | ConvertFrom-Json

  $vulnerablePackagesJsonPath = Join-Path $reportsRoot "dependency-vulnerabilities.json"
  $vulnerablePackages = Invoke-DotnetJson -Name "dependency-vulnerability-scan" -Arguments @("list", "STIGForge.sln", "package", "--vulnerable", "--include-transitive", "--format", "json") -LogPath (Join-Path $logsRoot "dependency-vulnerability-scan.log") -JsonPath $vulnerablePackagesJsonPath

  $vulnerabilityRows = New-Object System.Collections.Generic.List[object]
  foreach ($row in Get-PackageRowsFromDotnetList -DotnetListJson $vulnerablePackages -IncludeTransitive) {
    foreach ($v in @($row.vulnerabilities)) {
      $vulnerabilityRows.Add([pscustomobject]@{
        project = $row.project
        framework = $row.framework
        packageId = $row.packageId
        version = $row.version
        transitive = $row.transitive
        severity = [string]$v.severity
        advisoryUrl = [string]$v.advisoryurl
      })
    }
  }

  $allowedVulnerabilities = @($vulnPolicy.allowed)
  $suppressedVulnerabilities = New-Object System.Collections.Generic.List[object]
  $unresolvedVulnerabilities = New-Object System.Collections.Generic.List[object]

  foreach ($v in $vulnerabilityRows) {
    $match = $null
    foreach ($allowed in $allowedVulnerabilities) {
      if ([string]::Equals([string]$allowed.packageId, [string]$v.packageId, [StringComparison]::OrdinalIgnoreCase) -and
          [string]::Equals([string]$allowed.version, [string]$v.version, [StringComparison]::OrdinalIgnoreCase) -and
          [string]::Equals([string]$allowed.advisoryUrl, [string]$v.advisoryUrl, [StringComparison]::OrdinalIgnoreCase)) {
        $match = $allowed
        break
      }
    }

    if ($match) {
      $suppressedVulnerabilities.Add([pscustomobject]@{
        packageId = $v.packageId
        version = $v.version
        advisoryUrl = $v.advisoryUrl
        severity = $v.severity
        reason = [string]$match.reason
      })
    }
    else {
      $unresolvedVulnerabilities.Add($v)
    }
  }

  $dependenciesJsonPath = Join-Path $reportsRoot "dependencies.json"
  $dependencies = Invoke-DotnetJson -Name "dependency-inventory" -Arguments @("list", "STIGForge.sln", "package", "--format", "json") -LogPath (Join-Path $logsRoot "dependency-inventory.log") -JsonPath $dependenciesJsonPath

  $directPackages = Get-PackageRowsFromDotnetList -DotnetListJson $dependencies
  $uniqueDirectPackages = @($directPackages |
      Group-Object packageId, version |
      ForEach-Object {
        [pscustomobject]@{
          packageId = ($_.Group | Select-Object -First 1).packageId
          version = ($_.Group | Select-Object -First 1).version
        }
      } |
      Sort-Object packageId, version)

  $allowedLicenses = New-Object System.Collections.Generic.HashSet[string]([StringComparer]::OrdinalIgnoreCase)
  foreach ($expr in @($licensePolicy.allowedLicenseExpressions)) {
    [void]$allowedLicenses.Add(([string]$expr).ToUpperInvariant())
  }
  $allowedUrls = New-Object System.Collections.Generic.HashSet[string]([StringComparer]::OrdinalIgnoreCase)
  foreach ($url in @($licensePolicy.allowedLicenseUrls)) {
    [void]$allowedUrls.Add([string]$url)
  }
  $allowUnknownFor = New-Object System.Collections.Generic.HashSet[string]([StringComparer]::OrdinalIgnoreCase)
  foreach ($id in @($licensePolicy.allowUnknownForPackages)) {
    [void]$allowUnknownFor.Add([string]$id)
  }

  $licenseLookupCache = @{}
  $licenseResults = New-Object System.Collections.Generic.List[object]
  $licenseErrors = New-Object System.Collections.Generic.List[object]

  foreach ($pkg in $uniqueDirectPackages) {
    $cacheKey = ($pkg.packageId + "|" + $pkg.version).ToLowerInvariant()
    if (-not $licenseLookupCache.ContainsKey($cacheKey)) {
      if ($SkipLicenseLookup) {
        $licenseLookupCache[$cacheKey] = [pscustomobject]@{
          packageId = $pkg.packageId
          version = $pkg.version
          sourceUrl = ""
          licenseExpression = ""
          licenseUrl = ""
          error = "Skipped by -SkipLicenseLookup"
        }
      }
      else {
        $licenseLookupCache[$cacheKey] = Get-NuGetLicenseInfo -PackageId $pkg.packageId -Version $pkg.version
      }
    }

    $licenseInfo = $licenseLookupCache[$cacheKey]

    $status = "approved"
    $reason = ""

    if ($allowUnknownFor.Contains($pkg.packageId)) {
      $status = "approved"
      $reason = "Package is allowlisted for unknown license metadata"
    }
    elseif (-not [string]::IsNullOrWhiteSpace($licenseInfo.licenseExpression)) {
      if (Test-LicenseExpressionAllowed -Expression $licenseInfo.licenseExpression -AllowedLicenses $allowedLicenses) {
        $status = "approved"
        $reason = "License expression allowed"
      }
      else {
        $status = "rejected"
        $reason = "License expression is not in allowed policy"
      }
    }
    elseif (-not [string]::IsNullOrWhiteSpace($licenseInfo.licenseUrl)) {
      if ($allowedUrls.Contains($licenseInfo.licenseUrl)) {
        $status = "approved"
        $reason = "License URL allowed"
      }
      else {
        $status = "rejected"
        $reason = "License URL is not in allowed policy"
      }
    }
    elseif ($SkipLicenseLookup) {
      $status = "warning"
      $reason = "License lookup skipped"
    }
    else {
      $status = "rejected"
      $reason = if ([string]::IsNullOrWhiteSpace($licenseInfo.error)) { "No license metadata found" } else { "License lookup failed: " + $licenseInfo.error }
    }

    $result = [pscustomobject]@{
      packageId = $pkg.packageId
      version = $pkg.version
      status = $status
      reason = $reason
      licenseExpression = $licenseInfo.licenseExpression
      licenseUrl = $licenseInfo.licenseUrl
      sourceUrl = $licenseInfo.sourceUrl
      lookupError = $licenseInfo.error
    }

    $licenseResults.Add($result)
    if ($status -eq "rejected") {
      $licenseErrors.Add($result)
    }
  }

  $licenseReportPath = Join-Path $reportsRoot "license-compliance.json"
  $licenseResults | Sort-Object packageId, version | ConvertTo-Json -Depth 10 | Set-Content -Path $licenseReportPath -Encoding UTF8

  $secretFindings = @()
  if (-not $SkipSecrets) {
    Write-Host "[security-gate] secret-pattern-scan" -ForegroundColor Cyan
    try {
      $secretFindings = @(Find-SecretFindings -RepoRoot $RepositoryRoot -SecretsPolicy $secretsPolicy)
    }
    catch {
      Write-Host $_.Exception.Message -ForegroundColor Red
      Write-Host $_.ScriptStackTrace -ForegroundColor Red
      throw
    }
  }
  else {
    Write-Host "[security-gate] secret-pattern-scan skipped" -ForegroundColor Yellow
  }

  $secretsReportPath = Join-Path $reportsRoot "secrets-findings.json"
  $secretFindings | ConvertTo-Json -Depth 10 | Set-Content -Path $secretsReportPath -Encoding UTF8

  $summaryPath = Join-Path $reportsRoot "security-gate-summary.json"
  $licenseWarningCount = @($licenseResults | Where-Object { $_.status -eq "warning" }).Count
  $summary = [pscustomobject]@{
    generatedAtUtc = (Get-Date).ToUniversalTime().ToString("o")
    repository = $RepositoryRoot
    outputRoot = $outputRootFull
    dependencyVulnerabilities = [pscustomobject]@{
      total = $vulnerabilityRows.Count
      suppressed = $suppressedVulnerabilities.Count
      unresolved = $unresolvedVulnerabilities.Count
    }
    licenseCompliance = [pscustomobject]@{
      totalPackages = $licenseResults.Count
      rejected = $licenseErrors.Count
      warnings = $licenseWarningCount
      lookupSkipped = [bool]$SkipLicenseLookup
    }
    secrets = [pscustomobject]@{
      findings = $secretFindings.Count
      scanSkipped = [bool]$SkipSecrets
    }
    artifacts = [pscustomobject]@{
      vulnerabilities = $vulnerablePackagesJsonPath
      dependencies = $dependenciesJsonPath
      licenses = $licenseReportPath
      secrets = $secretsReportPath
    }
  }
  $summary | ConvertTo-Json -Depth 10 | Set-Content -Path $summaryPath -Encoding UTF8

  $markdownPath = Join-Path $reportsRoot "security-gate-report.md"
  $md = New-Object System.Text.StringBuilder
  [void]$md.AppendLine("# Security Gate Report")
  [void]$md.AppendLine()
  [void]$md.AppendLine("- Generated (UTC): $(Get-Date -Format 'yyyy-MM-ddTHH:mm:ssZ')")
  [void]$md.AppendLine("- Repository: $RepositoryRoot")
  [void]$md.AppendLine("- Vulnerabilities: $($unresolvedVulnerabilities.Count) unresolved ($($suppressedVulnerabilities.Count) suppressed)")
  [void]$md.AppendLine("- License compliance: $($licenseErrors.Count) rejected")
  [void]$md.AppendLine("- Secret findings: $($secretFindings.Count)")
  [void]$md.AppendLine()

  if ($unresolvedVulnerabilities.Count -gt 0) {
    [void]$md.AppendLine("## Unresolved Vulnerabilities")
    [void]$md.AppendLine()
    [void]$md.AppendLine("| Package | Version | Severity | Advisory | Project |")
    [void]$md.AppendLine("|---------|---------|----------|----------|---------|")
    foreach ($item in $unresolvedVulnerabilities | Sort-Object packageId, version, advisoryUrl) {
      [void]$md.AppendLine("| $($item.packageId) | $($item.version) | $($item.severity) | $($item.advisoryUrl) | $($item.project) |")
    }
    [void]$md.AppendLine()
  }

  if ($licenseErrors.Count -gt 0) {
    [void]$md.AppendLine("## License Policy Rejections")
    [void]$md.AppendLine()
    [void]$md.AppendLine("| Package | Version | Reason | Expression | License URL |")
    [void]$md.AppendLine("|---------|---------|--------|------------|-------------|")
    foreach ($item in $licenseErrors | Sort-Object packageId, version) {
      [void]$md.AppendLine("| $($item.packageId) | $($item.version) | $($item.reason) | $($item.licenseExpression) | $($item.licenseUrl) |")
    }
    [void]$md.AppendLine()
  }

  if ($secretFindings.Count -gt 0) {
    [void]$md.AppendLine("## Secret Findings")
    [void]$md.AppendLine()
    [void]$md.AppendLine("| Rule | File | Line | Excerpt |")
    [void]$md.AppendLine("|------|------|------|---------|")
    foreach ($finding in $secretFindings | Sort-Object file, line) {
      [void]$md.AppendLine("| $($finding.rule) | $($finding.file) | $($finding.line) | $($finding.excerpt) |")
    }
    [void]$md.AppendLine()
  }

  Set-Content -Path $markdownPath -Value $md.ToString() -Encoding UTF8

  Write-Host "[security-gate] report:   $markdownPath" -ForegroundColor Green
  Write-Host "[security-gate] summary:  $summaryPath" -ForegroundColor Green

  $failed = ($unresolvedVulnerabilities.Count -gt 0) -or ($licenseErrors.Count -gt 0) -or ($secretFindings.Count -gt 0)
  if ($failed) {
    Write-Host "[security-gate] Security gate failed." -ForegroundColor Red
    exit 1
  }

  Write-Host "[security-gate] Security gate passed." -ForegroundColor Green
  exit 0
}
finally {
  Pop-Location
}
