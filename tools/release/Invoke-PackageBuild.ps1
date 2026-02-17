param(
  [string]$Configuration = "Release",
  [string]$Runtime = "win-x64",
  [string]$OutputRoot = "",
  [string]$VersionTag = "",
  [string]$ReleaseGateRoot = "",
  [switch]$SkipDependencyInventory,
  [switch]$RequireSigning,
  [string]$SigningCertificateBase64 = "",
  [string]$SigningCertificatePassword = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Invoke-DotnetStep {
  param(
    [Parameter(Mandatory = $true)][string]$Name,
    [Parameter(Mandatory = $true)][string[]]$Arguments,
    [Parameter(Mandatory = $true)][string]$LogPath
  )

  Write-Host "[package] $Name" -ForegroundColor Cyan
  Write-Host "[package]   dotnet $($Arguments -join ' ')"

  $output = & dotnet @Arguments 2>&1
  $exitCode = $LASTEXITCODE
  $output | Out-File -FilePath $LogPath -Encoding UTF8

  if ($exitCode -ne 0) {
    throw "Step '$Name' failed with exit code $exitCode. See $LogPath"
  }
}

function Convert-ToRelativePath {
  param(
    [Parameter(Mandatory = $true)][string]$BasePath,
    [Parameter(Mandatory = $true)][string]$TargetPath
  )

  $baseResolved = (Resolve-Path $BasePath).Path
  $targetResolved = (Resolve-Path $TargetPath).Path

  $baseSuffix = if ($baseResolved.EndsWith([IO.Path]::DirectorySeparatorChar) -or $baseResolved.EndsWith([IO.Path]::AltDirectorySeparatorChar)) { "" } else { [string][IO.Path]::DirectorySeparatorChar }
  $baseUri = [System.Uri]::new(($baseResolved + $baseSuffix), [System.UriKind]::Absolute)
  $targetUri = [System.Uri]::new($targetResolved, [System.UriKind]::Absolute)

  return [System.Uri]::UnescapeDataString($baseUri.MakeRelativeUri($targetUri).ToString().Replace('/', [IO.Path]::DirectorySeparatorChar))
}

function Resolve-SignToolPath {
  $cmd = Get-Command signtool.exe -ErrorAction SilentlyContinue
  if ($cmd) { return $cmd.Source }

  $candidates = @(
    "${env:ProgramFiles(x86)}\Windows Kits\10\bin\x64\signtool.exe",
    "${env:ProgramFiles(x86)}\Windows Kits\10\bin\10.0.26100.0\x64\signtool.exe",
    "${env:ProgramFiles(x86)}\Windows Kits\10\bin\10.0.22621.0\x64\signtool.exe"
  )

  foreach ($candidate in $candidates) {
    if ([string]::IsNullOrWhiteSpace($candidate)) { continue }
    if (Test-Path $candidate) { return $candidate }
  }

  return ""
}

function Invoke-SignFile {
  param(
    [Parameter(Mandatory = $true)][string]$SignToolPath,
    [Parameter(Mandatory = $true)][string]$Thumbprint,
    [Parameter(Mandatory = $true)][string]$FilePath
  )

  $output = & "$SignToolPath" sign /fd SHA256 /td SHA256 /tr http://timestamp.digicert.com /sha1 $Thumbprint "$FilePath" 2>&1
  $exitCode = $LASTEXITCODE
  if ($exitCode -ne 0) {
    throw "Failed to sign '$FilePath'. signtool exit code: $exitCode. Output: $($output -join [Environment]::NewLine)"
  }
}

function Write-Checksums {
  param(
    [Parameter(Mandatory = $true)][string]$Root,
    [Parameter(Mandatory = $true)][string]$OutputPath,
    [string[]]$ExcludeRelativePaths = @()
  )

  $excludeLookup = @{}
  foreach ($exclude in $ExcludeRelativePaths) {
    if ([string]::IsNullOrWhiteSpace($exclude)) { continue }
    $normalized = $exclude.Replace([IO.Path]::AltDirectorySeparatorChar, [IO.Path]::DirectorySeparatorChar).ToLowerInvariant()
    $excludeLookup[$normalized] = $true
  }

  $lines = @()
  foreach ($file in Get-ChildItem -Path $Root -Recurse -File) {
    $relative = Convert-ToRelativePath -BasePath $Root -TargetPath $file.FullName
    $normalizedRelative = $relative.Replace([IO.Path]::AltDirectorySeparatorChar, [IO.Path]::DirectorySeparatorChar).ToLowerInvariant()
    if ($excludeLookup.ContainsKey($normalizedRelative)) {
      continue
    }

    $hash = Get-FileHash -Algorithm SHA256 -Path $file.FullName
    $lines += "{0}  {1}" -f $hash.Hash.ToLowerInvariant(), $relative
  }

  $lines | Sort-Object | Set-Content -Path $OutputPath -Encoding UTF8
}

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = (Resolve-Path (Join-Path $scriptRoot "..\..")).Path
$stamp = Get-Date -Format "yyyyMMdd-HHmmss"

if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
  $OutputRoot = Join-Path $repoRoot ".artifacts\release-package\$stamp"
}

$outputRootFull = [IO.Path]::GetFullPath($OutputRoot)
$logsRoot = Join-Path $outputRootFull "logs"
$publishRoot = Join-Path $outputRootFull "publish"
$bundleRoot = Join-Path $outputRootFull "bundle"
$manifestRoot = Join-Path $outputRootFull "manifest"
$sbomRoot = Join-Path $outputRootFull "sbom"

if ([string]::IsNullOrWhiteSpace($ReleaseGateRoot)) {
  $ReleaseGateRoot = Join-Path $repoRoot ".artifacts\release-gate\release-package"
}
$releaseGateRootFull = [IO.Path]::GetFullPath($ReleaseGateRoot)

New-Item -ItemType Directory -Path $outputRootFull -Force | Out-Null
New-Item -ItemType Directory -Path $logsRoot -Force | Out-Null
New-Item -ItemType Directory -Path $publishRoot -Force | Out-Null
New-Item -ItemType Directory -Path $bundleRoot -Force | Out-Null
New-Item -ItemType Directory -Path $manifestRoot -Force | Out-Null
New-Item -ItemType Directory -Path $sbomRoot -Force | Out-Null

$cliPublishDir = Join-Path $publishRoot "cli-$Runtime"
$appPublishDir = Join-Path $publishRoot "app-$Runtime"

$signingCertPath = ""
$importedCertThumbprint = ""
$signingPerformed = $false

Write-Host "[package] repository: $repoRoot"
Write-Host "[package] output:     $outputRootFull"
Write-Host "[package] gate root:  $releaseGateRootFull"

Push-Location $repoRoot

try {
  $dotnetInfoLogPath = Join-Path $logsRoot "dotnet-info.log"
  Invoke-DotnetStep -Name "dotnet-info" -Arguments @("--info") -LogPath $dotnetInfoLogPath

  Invoke-DotnetStep -Name "restore" -Arguments @("restore", "STIGForge.sln", "--nologo", "--runtime", $Runtime, "-p:EnableWindowsTargeting=true") -LogPath (Join-Path $logsRoot "restore.log")
  Invoke-DotnetStep -Name "build" -Arguments @("build", "STIGForge.sln", "--configuration", $Configuration, "--nologo", "--no-restore", "-p:EnableWindowsTargeting=true") -LogPath (Join-Path $logsRoot "build.log")

  Invoke-DotnetStep -Name "publish-cli" -Arguments @("publish", "src/STIGForge.Cli/STIGForge.Cli.csproj", "--configuration", $Configuration, "--runtime", $Runtime, "--self-contained", "false", "-p:EnableWindowsTargeting=true", "-o", $cliPublishDir) -LogPath (Join-Path $logsRoot "publish-cli.log")
  Invoke-DotnetStep -Name "publish-app" -Arguments @("publish", "src/STIGForge.App/STIGForge.App.csproj", "--configuration", $Configuration, "--runtime", $Runtime, "--self-contained", "false", "-p:EnableWindowsTargeting=true", "-o", $appPublishDir) -LogPath (Join-Path $logsRoot "publish-app.log")

  if ($RequireSigning) {
    if ([string]::IsNullOrWhiteSpace($SigningCertificateBase64)) {
      $SigningCertificateBase64 = $env:STIGFORGE_SIGN_CERT_BASE64
    }
    if ([string]::IsNullOrWhiteSpace($SigningCertificatePassword)) {
      $SigningCertificatePassword = $env:STIGFORGE_SIGN_CERT_PASSWORD
    }

    if ([string]::IsNullOrWhiteSpace($SigningCertificateBase64) -or [string]::IsNullOrWhiteSpace($SigningCertificatePassword)) {
      throw "Signing is required, but signing certificate inputs were not provided. Set -SigningCertificateBase64/-SigningCertificatePassword or STIGFORGE_SIGN_CERT_BASE64/STIGFORGE_SIGN_CERT_PASSWORD."
    }

    $signToolPath = Resolve-SignToolPath
    if ([string]::IsNullOrWhiteSpace($signToolPath)) {
      throw "Signing is required, but signtool.exe was not found. Install Windows SDK signing tools."
    }

    $certBytes = [Convert]::FromBase64String($SigningCertificateBase64)
    $signingCertPath = Join-Path $outputRootFull "signing-cert.pfx"
    [IO.File]::WriteAllBytes($signingCertPath, $certBytes)

    $securePassword = ConvertTo-SecureString -String $SigningCertificatePassword -AsPlainText -Force
    $cert = Import-PfxCertificate -FilePath $signingCertPath -CertStoreLocation "Cert:\CurrentUser\My" -Password $securePassword -Exportable
    $importedCertThumbprint = $cert.Thumbprint

    $targets = Get-ChildItem -Path $publishRoot -Recurse -File | Where-Object {
      $_.Extension -in @(".exe", ".dll")
    }

    foreach ($target in $targets) {
      Invoke-SignFile -SignToolPath $signToolPath -Thumbprint $importedCertThumbprint -FilePath $target.FullName
    }

    $signingPerformed = $true
  }

  $cliZipPath = Join-Path $bundleRoot "stigforge-cli-$Runtime.zip"
  $appZipPath = Join-Path $bundleRoot "stigforge-app-$Runtime.zip"
  $checksumsPath = Join-Path $manifestRoot "sha256-checksums.txt"
  $manifestPath = Join-Path $manifestRoot "release-package-manifest.json"
  $reproducibilityPath = Join-Path $manifestRoot "reproducibility-evidence.json"

  $isWindowsHost = if (Get-Variable -Name IsWindows -ErrorAction SilentlyContinue) {
    [bool]$IsWindows
  }
  else {
    $env:OS -eq "Windows_NT"
  }
  $sbomTarget = if ($isWindowsHost) { "STIGForge.sln" } else { "src/STIGForge.Cli/STIGForge.Cli.csproj" }
  $dependencyInventoryStatus = "skipped"
  $dependencyInventoryMessage = "Dependency inventory skipped by configuration"
  $dependencyInventoryPath = Join-Path $sbomRoot "dotnet-packages.json"

  if (-not $SkipDependencyInventory) {
    $dependencyArgs = @("list", $sbomTarget, "package", "--include-transitive", "--format", "json")
    $dependencyLogPath = Join-Path $logsRoot "sbom-dotnet-list-package.log"
    Invoke-DotnetStep -Name "sbom-dotnet-list-package" -Arguments $dependencyArgs -LogPath $dependencyLogPath
    Copy-Item -Path $dependencyLogPath -Destination $dependencyInventoryPath -Force
    $dependencyInventoryStatus = "generated"
    $dependencyInventoryMessage = "Dependency inventory generated from dotnet list package"
  }

  $releaseGateCatalog = @(
    [pscustomobject]@{ key = "releaseGateSummary"; relativePath = "report/release-gate-summary.json"; required = $true },
    [pscustomobject]@{ key = "releaseGateReport"; relativePath = "report/release-gate-report.md"; required = $true },
    [pscustomobject]@{ key = "releaseGateChecksums"; relativePath = "report/sha256-checksums.txt"; required = $true },
    [pscustomobject]@{ key = "securityGateSummary"; relativePath = "security/reports/security-gate-summary.json"; required = $true },
    [pscustomobject]@{ key = "securityGateReport"; relativePath = "security/reports/security-gate-report.md"; required = $true },
    [pscustomobject]@{ key = "quarterlySummary"; relativePath = "quarterly-pack/quarterly-pack-summary.json"; required = $true },
    [pscustomobject]@{ key = "quarterlyReport"; relativePath = "quarterly-pack/quarterly-pack-report.md"; required = $true },
    [pscustomobject]@{ key = "upgradeRebaseSummary"; relativePath = "upgrade-rebase/upgrade-rebase-summary.json"; required = $true },
    [pscustomobject]@{ key = "upgradeRebaseReport"; relativePath = "upgrade-rebase/upgrade-rebase-report.md"; required = $true },
    [pscustomobject]@{ key = "upgradeRebaseWpfWorkflowContract"; relativePath = "upgrade-rebase/upgrade-rebase-summary.json"; required = $true; summaryStep = "upgrade-rebase-wpf-workflow-contract" },
    [pscustomobject]@{ key = "upgradeRebaseWpfSeverityContract"; relativePath = "upgrade-rebase/upgrade-rebase-summary.json"; required = $true; summaryStep = "upgrade-rebase-wpf-severity-contract" },
    [pscustomobject]@{ key = "upgradeRebaseWpfRecoveryContract"; relativePath = "upgrade-rebase/upgrade-rebase-summary.json"; required = $true; summaryStep = "upgrade-rebase-wpf-recovery-contract" }
  )

  $recoveryCommand = "powershell -NoProfile -ExecutionPolicy Bypass -File .\\tools\\release\\Invoke-ReleaseGate.ps1 -Configuration $Configuration -OutputRoot $ReleaseGateRoot"
  $contractValidatorPath = Join-Path $scriptRoot "Test-ReleaseEvidenceContract.ps1"
  $contractReportPath = Join-Path $releaseGateRootFull "report/release-evidence-contract-report.md"

  $releaseGateEvidence = @()
  $releaseGateStatus = "unavailable"
  $releaseGateMessage = "Release evidence contract not evaluated"

  try {
    & $contractValidatorPath -EvidenceRoot $releaseGateRootFull -ReportPath $contractReportPath -RecoveryCommand $recoveryCommand
    $releaseGateStatus = "linked"
    $releaseGateMessage = "Mandatory release evidence contract passed"
  }
  catch {
    $releaseGateStatus = "blocked"
    $releaseGateMessage = $_.Exception.Message

    $blockedManifest = [pscustomobject]@{
      generatedAtUtc = (Get-Date).ToUniversalTime().ToString("o")
      versionTag = $VersionTag
      configuration = $Configuration
      runtime = $Runtime
      repository = $repoRoot
      outputRoot = $outputRootFull
      signing = [pscustomobject]@{
        required = [bool]$RequireSigning
        performed = $signingPerformed
      }
      reproducibility = [pscustomobject]@{
        checksums = $checksumsPath
        dependencyInventory = [pscustomobject]@{
          status = $dependencyInventoryStatus
          message = $dependencyInventoryMessage
          path = $dependencyInventoryPath
          target = $sbomTarget
        }
        releaseGateEvidence = [pscustomobject]@{
          status = $releaseGateStatus
          message = $releaseGateMessage
          root = $releaseGateRootFull
        }
        report = $reproducibilityPath
      }
      artifacts = [pscustomobject]@{
        cliPublish = $cliPublishDir
        appPublish = $appPublishDir
        cliZip = ""
        appZip = ""
        dependencyInventory = $dependencyInventoryPath
        checksums = $checksumsPath
        reproducibility = $reproducibilityPath
      }
    }

    $blockedReproducibility = [pscustomobject]@{
      generatedAtUtc = (Get-Date).ToUniversalTime().ToString("o")
      repository = $repoRoot
      configuration = $Configuration
      runtime = $Runtime
      versionTag = $VersionTag
      outputRoot = $outputRootFull
      dotnetInfoLog = $dotnetInfoLogPath
      dependencyInventory = [pscustomobject]@{
        status = $dependencyInventoryStatus
        message = $dependencyInventoryMessage
        path = $dependencyInventoryPath
        target = $sbomTarget
      }
      releaseGateEvidence = [pscustomobject]@{
        status = $releaseGateStatus
        message = $releaseGateMessage
        root = $releaseGateRootFull
        reportPath = $contractReportPath
        artifacts = @()
      }
      packageArtifacts = [pscustomobject]@{
        cliZip = ""
        appZip = ""
        manifest = $manifestPath
      }
      checksumsPath = $checksumsPath
      blocked = $true
    }

    $blockedManifest | ConvertTo-Json -Depth 6 | Set-Content -Path $manifestPath -Encoding UTF8
    $blockedReproducibility | ConvertTo-Json -Depth 8 | Set-Content -Path $reproducibilityPath -Encoding UTF8

    throw "$releaseGateMessage`nnext command: $recoveryCommand"
  }

  if (Test-Path -LiteralPath $releaseGateRootFull) {
    foreach ($artifact in $releaseGateCatalog) {
      $artifactPath = Join-Path $releaseGateRootFull $artifact.relativePath
      $exists = Test-Path -LiteralPath $artifactPath
      $hashValue = ""
      $summaryStep = if ($artifact.PSObject.Properties.Name -contains 'summaryStep') { [string]$artifact.summaryStep } else { "" }

      if ($exists) {
        $hashValue = (Get-FileHash -Algorithm SHA256 -Path $artifactPath).Hash.ToLowerInvariant()
      }

      $releaseGateEvidence += [pscustomobject]@{
        key = $artifact.key
        required = [bool]$artifact.required
        exists = [bool]$exists
        path = $artifactPath
        sha256 = $hashValue
        summaryStep = $summaryStep
        summaryStepExists = $null
        summaryStepSucceeded = $null
      }
    }
  }

  if (Test-Path $cliZipPath) { Remove-Item -Path $cliZipPath -Force }
  if (Test-Path $appZipPath) { Remove-Item -Path $appZipPath -Force }
  Compress-Archive -Path (Join-Path $cliPublishDir "*") -DestinationPath $cliZipPath
  Compress-Archive -Path (Join-Path $appPublishDir "*") -DestinationPath $appZipPath

  $gitCommit = ""
  if (Get-Command git -ErrorAction SilentlyContinue) {
    $gitCommitOutput = & git rev-parse HEAD 2>$null
    if ($LASTEXITCODE -eq 0 -and -not [string]::IsNullOrWhiteSpace($gitCommitOutput)) {
      $gitCommit = ($gitCommitOutput | Select-Object -First 1).Trim()
    }
  }

  $manifest = [pscustomobject]@{
    generatedAtUtc = (Get-Date).ToUniversalTime().ToString("o")
    versionTag = $VersionTag
    configuration = $Configuration
    runtime = $Runtime
    repository = $repoRoot
    gitCommit = $gitCommit
    outputRoot = $outputRootFull
    signing = [pscustomobject]@{
      required = [bool]$RequireSigning
      performed = $signingPerformed
    }
    reproducibility = [pscustomobject]@{
      checksums = $checksumsPath
      dependencyInventory = [pscustomobject]@{
        status = $dependencyInventoryStatus
        message = $dependencyInventoryMessage
        path = $dependencyInventoryPath
        target = $sbomTarget
      }
      releaseGateEvidence = [pscustomobject]@{
        status = $releaseGateStatus
        message = $releaseGateMessage
        root = $releaseGateRootFull
      }
      report = $reproducibilityPath
    }
    artifacts = [pscustomobject]@{
      cliPublish = $cliPublishDir
      appPublish = $appPublishDir
      cliZip = $cliZipPath
      appZip = $appZipPath
      dependencyInventory = $dependencyInventoryPath
      checksums = $checksumsPath
      reproducibility = $reproducibilityPath
    }
  }

  $reproducibility = [pscustomobject]@{
    generatedAtUtc = (Get-Date).ToUniversalTime().ToString("o")
    repository = $repoRoot
    gitCommit = $gitCommit
    configuration = $Configuration
    runtime = $Runtime
    versionTag = $VersionTag
    outputRoot = $outputRootFull
    dotnetInfoLog = $dotnetInfoLogPath
    buildLogs = [pscustomobject]@{
      restore = Join-Path $logsRoot "restore.log"
      build = Join-Path $logsRoot "build.log"
      publishCli = Join-Path $logsRoot "publish-cli.log"
      publishApp = Join-Path $logsRoot "publish-app.log"
    }
    dependencyInventory = [pscustomobject]@{
      status = $dependencyInventoryStatus
      message = $dependencyInventoryMessage
      path = $dependencyInventoryPath
      target = $sbomTarget
    }
    releaseGateEvidence = [pscustomobject]@{
      status = $releaseGateStatus
      message = $releaseGateMessage
      root = $releaseGateRootFull
      artifacts = @($releaseGateEvidence)
    }
    packageArtifacts = [pscustomobject]@{
      cliZip = $cliZipPath
      appZip = $appZipPath
      manifest = $manifestPath
    }
    checksumsPath = $checksumsPath
  }

  $manifest | ConvertTo-Json -Depth 6 | Set-Content -Path $manifestPath -Encoding UTF8
  $reproducibility | ConvertTo-Json -Depth 8 | Set-Content -Path $reproducibilityPath -Encoding UTF8

  $checksumsRelativePath = "manifest$([IO.Path]::DirectorySeparatorChar)sha256-checksums.txt"
  Write-Checksums -Root $outputRootFull -OutputPath $checksumsPath -ExcludeRelativePaths @($checksumsRelativePath)

  Write-Host "[package] cli zip:    $cliZipPath" -ForegroundColor Green
  Write-Host "[package] app zip:    $appZipPath" -ForegroundColor Green
  Write-Host "[package] dependency: $dependencyInventoryPath ($dependencyInventoryStatus)" -ForegroundColor Green
  Write-Host "[package] evidence:   $reproducibilityPath" -ForegroundColor Green
  Write-Host "[package] gate link:  $releaseGateStatus ($releaseGateMessage)" -ForegroundColor Green
  Write-Host "[package] manifest:   $manifestPath" -ForegroundColor Green
  Write-Host "[package] checksums:  $checksumsPath" -ForegroundColor Green
}
finally {
  if (-not [string]::IsNullOrWhiteSpace($importedCertThumbprint)) {
    try {
      $certItem = Get-Item "Cert:\CurrentUser\My\$importedCertThumbprint" -ErrorAction SilentlyContinue
      if ($certItem) { Remove-Item -Path $certItem.PSPath -DeleteKey -Force }
    }
    catch { }
  }

  if (-not [string]::IsNullOrWhiteSpace($signingCertPath) -and (Test-Path $signingCertPath)) {
    try { Remove-Item -Path $signingCertPath -Force } catch { }
  }

  Pop-Location
}
