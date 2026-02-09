param(
  [string]$Configuration = "Release",
  [string]$OutputRoot = "",
  [switch]$SecurityStrict,
  [switch]$EnableNetworkLicenseLookup,
  [switch]$SkipSbom,
  [string]$QuarterlyPackPath = "",
  [switch]$SkipQuarterlyRegressionPack
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$script:CommandShell = ""
if (Get-Command powershell -ErrorAction SilentlyContinue) {
  $script:CommandShell = "powershell"
}
elseif (Get-Command pwsh -ErrorAction SilentlyContinue) {
  $script:CommandShell = "pwsh"
}
else {
  throw "Neither 'powershell' nor 'pwsh' was found on PATH."
}

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
  $dotnetCandidate = Join-Path $HOME ".dotnet/dotnet"
  if (Test-Path $dotnetCandidate) {
    $dotnetDir = [IO.Path]::GetDirectoryName($dotnetCandidate)
    $env:PATH = "$dotnetDir$([IO.Path]::PathSeparator)$env:PATH"
  }
}

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
  throw "'dotnet' was not found on PATH. Install .NET SDK or add dotnet to PATH."
}

function New-Step {
  param(
    [Parameter(Mandatory = $true)][string]$Name,
    [Parameter(Mandatory = $true)][string]$Command,
    [Parameter(Mandatory = $true)][string]$LogPath
  )

  Write-Host "[release-gate] $Name" -ForegroundColor Cyan
  Write-Host "[release-gate]   $Command"

  $started = Get-Date
  $output = & $script:CommandShell -NoProfile -ExecutionPolicy Bypass -Command $Command 2>&1
  $exitCode = $LASTEXITCODE
  $finished = Get-Date

  $output | Out-File -FilePath $LogPath -Encoding UTF8

  return [pscustomobject]@{
    Name = $Name
    Command = $Command
    LogPath = $LogPath
    StartedAt = $started
    FinishedAt = $finished
    ExitCode = $exitCode
    Succeeded = ($exitCode -eq 0)
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

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = (Resolve-Path (Join-Path $scriptRoot "..\..")).Path
$stamp = Get-Date -Format "yyyyMMdd-HHmmss"

if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
  $OutputRoot = Join-Path $repoRoot ".artifacts\release-gate\$stamp"
}

if ([string]::IsNullOrWhiteSpace($QuarterlyPackPath)) {
  $QuarterlyPackPath = ".\tools\release\quarterly-regression-pack.psd1"
}

$logRoot = Join-Path $OutputRoot "logs"
$reportRoot = Join-Path $OutputRoot "report"
$sbomRoot = Join-Path $OutputRoot "sbom"
$upgradeRebaseRoot = Join-Path $OutputRoot "upgrade-rebase"
$upgradeRebaseResultsRoot = Join-Path $upgradeRebaseRoot "results"

New-Item -ItemType Directory -Force -Path $OutputRoot | Out-Null
New-Item -ItemType Directory -Force -Path $logRoot | Out-Null
New-Item -ItemType Directory -Force -Path $reportRoot | Out-Null
New-Item -ItemType Directory -Force -Path $upgradeRebaseRoot | Out-Null
New-Item -ItemType Directory -Force -Path $upgradeRebaseResultsRoot | Out-Null

Write-Host "[release-gate] repository: $repoRoot"
Write-Host "[release-gate] artifacts:  $OutputRoot"
Write-Host "[release-gate] shell:      $script:CommandShell"
Write-Host "[release-gate] dotnet:     $((Get-Command dotnet).Source)"

$sbomTarget = "STIGForge.sln"
if (-not $IsWindows) {
  $sbomTarget = "src/STIGForge.Cli/STIGForge.Cli.csproj"
  Write-Host "[release-gate] sbom target: $sbomTarget (non-Windows host)"
}
else {
  Write-Host "[release-gate] sbom target: $sbomTarget"
}

Push-Location $repoRoot

try {
  $steps = @()
  $securityOutputRoot = Join-Path $OutputRoot "security"
  $quarterlyOutputRoot = Join-Path $OutputRoot "quarterly-pack"
  $quarterlySummaryPath = Join-Path $quarterlyOutputRoot "quarterly-pack-summary.json"
  $quarterlyReportPath = Join-Path $quarterlyOutputRoot "quarterly-pack-report.md"
  $quarterlyPackStatus = "skipped"
  $quarterlyPackMessage = "Quarterly regression pack skipped by configuration"
  $quarterlyPackSummary = $null
  $upgradeRebaseSummaryPath = Join-Path $upgradeRebaseRoot "upgrade-rebase-summary.json"
  $upgradeRebaseReportPath = Join-Path $upgradeRebaseRoot "upgrade-rebase-report.md"
  $upgradeRebaseStatus = "pending"
  $upgradeRebaseMessage = "Upgrade/rebase validation not evaluated"

  $steps += New-Step -Name "dotnet-info" -Command "dotnet --info" -LogPath (Join-Path $logRoot "dotnet-info.log")
  $steps += New-Step -Name "restore" -Command "dotnet restore STIGForge.sln --nologo -p:EnableWindowsTargeting=true" -LogPath (Join-Path $logRoot "restore.log")
  $steps += New-Step -Name "build" -Command "dotnet build STIGForge.sln --configuration $Configuration --nologo --no-restore -p:EnableWindowsTargeting=true" -LogPath (Join-Path $logRoot "build.log")
  if (-not $SkipQuarterlyRegressionPack) {
    $quarterlyPackCommand = @(
      "$script:CommandShell",
      "-NoProfile",
      "-ExecutionPolicy Bypass",
      "-File .\tools\release\Run-QuarterlyRegressionPack.ps1",
      "-PackPath '$QuarterlyPackPath'",
      "-OutputRoot '$quarterlyOutputRoot'"
    ) -join " "
    $steps += New-Step -Name "quarterly-compatibility-pack" -Command $quarterlyPackCommand -LogPath (Join-Path $logRoot "quarterly-compatibility-pack.log")
  }
  $securityGateArgs = @(
    "$script:CommandShell",
    "-NoProfile",
    "-ExecutionPolicy Bypass",
    "-File .\tools\release\Invoke-SecurityGate.ps1",
    "-OutputRoot '$securityOutputRoot'"
  )
  if (-not $SkipQuarterlyRegressionPack) {
    $securityGateArgs += "-QuarterlyDriftSummaryPath '$quarterlySummaryPath'"
    $securityGateArgs += "-QuarterlyDriftReportPath '$quarterlyReportPath'"
  }
  if ($SecurityStrict) {
    $securityGateArgs += "-Strict"
  }
  if ($EnableNetworkLicenseLookup) {
    $securityGateArgs += "-EnableNetworkLicenseLookup"
  }
  $steps += New-Step -Name "security-gate" -Command ($securityGateArgs -join " ") -LogPath (Join-Path $logRoot "security-gate.log")
  $steps += New-Step -Name "test-unit" -Command "dotnet test tests/STIGForge.UnitTests/STIGForge.UnitTests.csproj --configuration $Configuration --framework net8.0 --nologo --no-build --filter 'FullyQualifiedName!~RebootCoordinator'" -LogPath (Join-Path $logRoot "test-unit.log")
  $steps += New-Step -Name "test-integration" -Command "dotnet test tests/STIGForge.IntegrationTests/STIGForge.IntegrationTests.csproj --configuration $Configuration --framework net8.0 --nologo --no-build --filter 'FullyQualifiedName!~E2E'" -LogPath (Join-Path $logRoot "test-integration.log")
  $steps += New-Step -Name "test-e2e" -Command "dotnet test tests/STIGForge.IntegrationTests/STIGForge.IntegrationTests.csproj --configuration $Configuration --framework net8.0 --nologo --no-build --filter 'FullyQualifiedName~E2E'" -LogPath (Join-Path $logRoot "test-e2e.log")

  $upgradeRebaseDiffCommand = "dotnet test tests/STIGForge.UnitTests/STIGForge.UnitTests.csproj --configuration $Configuration --framework net8.0 --nologo --no-build --filter 'FullyQualifiedName~BaselineDiffServiceTests' --logger `"trx;LogFileName=upgrade-rebase-diff-contract.trx`" --results-directory '$upgradeRebaseResultsRoot'"
  $upgradeRebaseOverlayCommand = "dotnet test tests/STIGForge.UnitTests/STIGForge.UnitTests.csproj --configuration $Configuration --framework net8.0 --nologo --no-build --filter 'FullyQualifiedName~OverlayRebaseServiceTests' --logger `"trx;LogFileName=upgrade-rebase-overlay-contract.trx`" --results-directory '$upgradeRebaseResultsRoot'"
  $upgradeRebaseParityCommand = "dotnet test tests/STIGForge.UnitTests/STIGForge.UnitTests.csproj --configuration $Configuration --framework net8.0 --nologo --no-build --filter 'FullyQualifiedName~BundleMissionSummaryServiceTests' --logger `"trx;LogFileName=upgrade-rebase-parity-contract.trx`" --results-directory '$upgradeRebaseResultsRoot'"
  $upgradeRebaseCliCommand = "dotnet test tests/STIGForge.IntegrationTests/STIGForge.IntegrationTests.csproj --configuration $Configuration --framework net8.0 --nologo --no-build --filter 'FullyQualifiedName~CliCommandTests.DiffPacks|FullyQualifiedName~CliCommandTests.RebaseOverlay' --logger `"trx;LogFileName=upgrade-rebase-cli-contract.trx`" --results-directory '$upgradeRebaseResultsRoot'"
  $upgradeRebaseRollbackCommand = "dotnet test tests/STIGForge.UnitTests/STIGForge.UnitTests.csproj --configuration $Configuration --framework net8.0 --nologo --no-build --filter 'FullyQualifiedName~ApplyRunnerTests' --logger `"trx;LogFileName=upgrade-rebase-rollback-safety.trx`" --results-directory '$upgradeRebaseResultsRoot'"

  $steps += New-Step -Name "upgrade-rebase-diff-contract" -Command $upgradeRebaseDiffCommand -LogPath (Join-Path $logRoot "upgrade-rebase-diff-contract.log")
  $steps += New-Step -Name "upgrade-rebase-overlay-contract" -Command $upgradeRebaseOverlayCommand -LogPath (Join-Path $logRoot "upgrade-rebase-overlay-contract.log")
  $steps += New-Step -Name "upgrade-rebase-parity-contract" -Command $upgradeRebaseParityCommand -LogPath (Join-Path $logRoot "upgrade-rebase-parity-contract.log")
  $steps += New-Step -Name "upgrade-rebase-cli-contract" -Command $upgradeRebaseCliCommand -LogPath (Join-Path $logRoot "upgrade-rebase-cli-contract.log")
  $steps += New-Step -Name "upgrade-rebase-rollback-safety" -Command $upgradeRebaseRollbackCommand -LogPath (Join-Path $logRoot "upgrade-rebase-rollback-safety.log")

  $failed = @($steps | Where-Object { -not $_.Succeeded })

  if ($SkipQuarterlyRegressionPack) {
    $quarterlyPackStatus = "skipped"
    $quarterlyPackMessage = "Quarterly regression pack skipped because -SkipQuarterlyRegressionPack was set"
  }
  elseif (Test-Path -LiteralPath $quarterlySummaryPath) {
    $quarterlyPackSummary = Get-Content -Path $quarterlySummaryPath -Raw | ConvertFrom-Json
    $quarterlyPackStatus = if ([bool]$quarterlyPackSummary.overallPassed) { "passed" } else { "failed" }
    $quarterlyPackMessage = "decision=$($quarterlyPackSummary.decision); warnings=$($quarterlyPackSummary.fixtures.warnings); failed=$($quarterlyPackSummary.fixtures.failed)"
  }
  else {
    $quarterlyPackStatus = "unavailable"
    $quarterlyPackMessage = "Quarterly regression pack summary was not generated"
  }

  $sbomStatus = "skipped"
  $sbomMessage = "SBOM generation skipped by default"
  $sbomPath = ""

  if (-not $SkipSbom -and $failed.Count -eq 0) {
    New-Item -ItemType Directory -Force -Path $sbomRoot | Out-Null
    $sbomPath = Join-Path $sbomRoot "dotnet-packages.json"
    $sbomStep = New-Step -Name "sbom-dotnet-list-package" -Command "dotnet list $sbomTarget package --include-transitive --format json" -LogPath (Join-Path $logRoot "sbom-dotnet-list-package.log")
    if ($sbomStep.Succeeded) {
      Copy-Item -Path $sbomStep.LogPath -Destination $sbomPath -Force
      $sbomStatus = "generated"
      $sbomMessage = "Dependency inventory generated from dotnet list package"
      $steps += $sbomStep
    }
    else {
      $sbomStatus = "failed"
      $sbomMessage = "Failed to generate dependency inventory"
      $steps += $sbomStep
      $failed += $sbomStep
    }
  }
  elseif ($SkipSbom) {
    $sbomMessage = "SBOM generation skipped because -SkipSbom was set"
  }
  elseif ($failed.Count -gt 0) {
    $sbomMessage = "SBOM generation skipped because earlier gate steps failed"
  }

  $upgradeRebaseSteps = @($steps | Where-Object { $_.Name -like "upgrade-rebase-*" })
  if ($upgradeRebaseSteps.Count -eq 0) {
    $upgradeRebaseStatus = "unavailable"
    $upgradeRebaseMessage = "Upgrade/rebase validation steps were not configured"
  }
  else {
    $upgradeRebaseFailures = @($upgradeRebaseSteps | Where-Object { -not $_.Succeeded })
    if ($upgradeRebaseFailures.Count -eq 0) {
      $upgradeRebaseStatus = "passed"
      $upgradeRebaseMessage = "All upgrade/rebase validation contracts passed"
    }
    else {
      $upgradeRebaseStatus = "failed"
      $upgradeRebaseMessage = "Failed steps: $($upgradeRebaseFailures.Name -join ', ')"
    }
  }

  $upgradeRebaseReport = New-Object System.Text.StringBuilder
  [void]$upgradeRebaseReport.AppendLine("# Upgrade and Rebase Validation")
  [void]$upgradeRebaseReport.AppendLine()
  [void]$upgradeRebaseReport.AppendLine("- Timestamp (UTC): $(Get-Date -Format 'yyyy-MM-ddTHH:mm:ssZ')")
  [void]$upgradeRebaseReport.AppendLine("- Status: $upgradeRebaseStatus")
  [void]$upgradeRebaseReport.AppendLine("- Summary: $upgradeRebaseMessage")
  [void]$upgradeRebaseReport.AppendLine()
  [void]$upgradeRebaseReport.AppendLine("## Required Evidence Areas")
  [void]$upgradeRebaseReport.AppendLine()
  [void]$upgradeRebaseReport.AppendLine("- Baseline to target diff contract")
  [void]$upgradeRebaseReport.AppendLine("- Overlay rebase behavior contract")
  [void]$upgradeRebaseReport.AppendLine("- Mission summary parity contract (CLI/WPF severity semantics)")
  [void]$upgradeRebaseReport.AppendLine("- CLI diff/rebase integration contract")
  [void]$upgradeRebaseReport.AppendLine("- Rollback safety and operator-decision guardrails")
  [void]$upgradeRebaseReport.AppendLine()
  [void]$upgradeRebaseReport.AppendLine("## Step Results")
  [void]$upgradeRebaseReport.AppendLine()
  [void]$upgradeRebaseReport.AppendLine("| Step | Status | Exit Code | Log |")
  [void]$upgradeRebaseReport.AppendLine("|------|--------|-----------|-----|")
  foreach ($step in $upgradeRebaseSteps) {
    $status = if ($step.Succeeded) { "PASS" } else { "FAIL" }
    $relativeLog = Convert-ToRelativePath -BasePath $upgradeRebaseRoot -TargetPath $step.LogPath
    [void]$upgradeRebaseReport.AppendLine("| $($step.Name) | $status | $($step.ExitCode) | $relativeLog |")
  }
  Set-Content -Path $upgradeRebaseReportPath -Value $upgradeRebaseReport.ToString() -Encoding UTF8

  $upgradeRebaseSummary = [pscustomobject]@{
    generatedAtUtc = (Get-Date).ToUniversalTime().ToString("o")
    status = $upgradeRebaseStatus
    message = $upgradeRebaseMessage
    outputRoot = $upgradeRebaseRoot
    reportPath = $upgradeRebaseReportPath
    resultsRoot = $upgradeRebaseResultsRoot
    requiredEvidence = @(
      "baseline-to-target diff contract",
      "overlay rebase behavior contract",
      "mission summary parity contract (cli/wpf severity semantics)",
      "cli diff/rebase integration contract",
      "rollback safety and operator decision guardrails"
    )
    steps = @($upgradeRebaseSteps | ForEach-Object {
      [pscustomobject]@{
        name = $_.Name
        command = $_.Command
        exitCode = $_.ExitCode
        succeeded = $_.Succeeded
        startedAt = $_.StartedAt.ToUniversalTime().ToString("o")
        finishedAt = $_.FinishedAt.ToUniversalTime().ToString("o")
        logPath = $_.LogPath
      }
    })
  }
  $upgradeRebaseSummary | ConvertTo-Json -Depth 6 | Set-Content -Path $upgradeRebaseSummaryPath -Encoding UTF8

  $reportPath = Join-Path $reportRoot "release-gate-report.md"
  $summaryPath = Join-Path $reportRoot "release-gate-summary.json"
  $checksumsPath = Join-Path $reportRoot "sha256-checksums.txt"
  $upgradeRebaseReportRelative = Convert-ToRelativePath -BasePath $reportRoot -TargetPath $upgradeRebaseReportPath
  $upgradeRebaseSummaryRelative = Convert-ToRelativePath -BasePath $reportRoot -TargetPath $upgradeRebaseSummaryPath

  $overallPass = ($failed.Count -eq 0)

  $report = New-Object System.Text.StringBuilder
  [void]$report.AppendLine("# STIGForge Release Gate")
  [void]$report.AppendLine()
  [void]$report.AppendLine("- Timestamp (UTC): $(Get-Date -Format 'yyyy-MM-ddTHH:mm:ssZ')")
  [void]$report.AppendLine("- Configuration: $Configuration")
  [void]$report.AppendLine("- Repository: $repoRoot")
  [void]$report.AppendLine("- Result: $(if ($overallPass) { 'PASS' } else { 'FAIL' })")
  [void]$report.AppendLine("- SBOM: $sbomStatus ($sbomMessage)")
  [void]$report.AppendLine("- Quarterly compatibility: $quarterlyPackStatus ($quarterlyPackMessage)")
  [void]$report.AppendLine("- Upgrade/rebase validation: $upgradeRebaseStatus ($upgradeRebaseMessage)")
  [void]$report.AppendLine("- Upgrade/rebase summary: $upgradeRebaseSummaryRelative")
  [void]$report.AppendLine("- Upgrade/rebase report: $upgradeRebaseReportRelative")
  [void]$report.AppendLine()
  [void]$report.AppendLine("## Step Results")
  [void]$report.AppendLine()
  [void]$report.AppendLine("| Step | Status | Exit Code | Log |")
  [void]$report.AppendLine("|------|--------|-----------|-----|")

  foreach ($step in $steps) {
    $status = if ($step.Succeeded) { "PASS" } else { "FAIL" }
    $relativeLog = Convert-ToRelativePath -BasePath $reportRoot -TargetPath $step.LogPath
    [void]$report.AppendLine("| $($step.Name) | $status | $($step.ExitCode) | $relativeLog |")
  }

  [void]$report.AppendLine()
  [void]$report.AppendLine("## Manual Release Checks")
  [void]$report.AppendLine()
  [void]$report.AppendLine("- Validate signing for MSI/MSIX and CLI binaries")
  [void]$report.AppendLine("- Validate upgrade/rebase report confirms diff, overlay, and rollback safety coverage")
  [void]$report.AppendLine("- Validate upgrade and rollback path from previous release")
  [void]$report.AppendLine("- Validate VM smoke matrix (Win10/Win11/Server)")
  [void]$report.AppendLine("- Publish release notes, checksums, and SBOM artifacts")

  Set-Content -Path $reportPath -Value $report.ToString() -Encoding UTF8

  $summary = [pscustomobject]@{
    generatedAtUtc = (Get-Date).ToUniversalTime().ToString("o")
    configuration = $Configuration
    repository = $repoRoot
    outputRoot = $OutputRoot
    securityGateMode = [pscustomobject]@{
      strict = [bool]$SecurityStrict
      deterministicOffline = [bool](-not $EnableNetworkLicenseLookup)
    }
    quarterlyCompatibility = [pscustomobject]@{
      status = $quarterlyPackStatus
      message = $quarterlyPackMessage
      summaryPath = $quarterlySummaryPath
      reportPath = $quarterlyReportPath
      decision = if ($null -ne $quarterlyPackSummary) { [string]$quarterlyPackSummary.decision } else { "" }
      warningCount = if ($null -ne $quarterlyPackSummary) { [int]$quarterlyPackSummary.fixtures.warnings } else { 0 }
      failureCount = if ($null -ne $quarterlyPackSummary) { [int]$quarterlyPackSummary.fixtures.failed } else { 0 }
    }
    upgradeRebaseValidation = [pscustomobject]@{
      status = $upgradeRebaseStatus
      message = $upgradeRebaseMessage
      summaryPath = $upgradeRebaseSummaryPath
      reportPath = $upgradeRebaseReportPath
      requiredEvidence = @(
        "baseline-to-target diff contract",
        "overlay rebase behavior contract",
        "mission summary parity contract (cli/wpf severity semantics)",
        "cli diff/rebase integration contract",
        "rollback safety and operator decision guardrails"
      )
    }
    overallPassed = $overallPass
    sbom = [pscustomobject]@{
      status = $sbomStatus
      message = $sbomMessage
      path = $sbomPath
    }
    steps = @($steps | ForEach-Object {
      [pscustomobject]@{
        name = $_.Name
        command = $_.Command
        exitCode = $_.ExitCode
        succeeded = $_.Succeeded
        startedAt = $_.StartedAt.ToUniversalTime().ToString("o")
        finishedAt = $_.FinishedAt.ToUniversalTime().ToString("o")
        logPath = $_.LogPath
      }
    })
  }
  $summary | ConvertTo-Json -Depth 6 | Set-Content -Path $summaryPath -Encoding UTF8

  $artifactFiles = Get-ChildItem -Path $OutputRoot -Recurse -File
  $checksumLines = @()
  foreach ($file in $artifactFiles) {
    $hash = Get-FileHash -Algorithm SHA256 -Path $file.FullName
    $relative = Convert-ToRelativePath -BasePath $OutputRoot -TargetPath $file.FullName
    $checksumLines += "{0}  {1}" -f $hash.Hash.ToLowerInvariant(), $relative
  }
  $checksumLines | Sort-Object | Set-Content -Path $checksumsPath -Encoding UTF8

  Write-Host "[release-gate] report:    $reportPath" -ForegroundColor Green
  Write-Host "[release-gate] summary:   $summaryPath" -ForegroundColor Green
  Write-Host "[release-gate] checksums: $checksumsPath" -ForegroundColor Green

  if (-not $overallPass) {
    Write-Host "[release-gate] One or more gate steps failed." -ForegroundColor Red
    exit 1
  }

  Write-Host "[release-gate] Release gate passed." -ForegroundColor Green
  exit 0
}
finally {
  Pop-Location
}
