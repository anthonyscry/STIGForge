param(
  [string]$Configuration = "Release",
  [string]$OutputRoot = "",
  [switch]$SkipSbom
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

  $baseFull = [IO.Path]::GetFullPath($BasePath)
  $targetFull = [IO.Path]::GetFullPath($TargetPath)
  return [IO.Path]::GetRelativePath($baseFull, $targetFull)
}

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Resolve-Path (Join-Path $scriptRoot "..\..")
$stamp = Get-Date -Format "yyyyMMdd-HHmmss"

if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
  $OutputRoot = Join-Path $repoRoot ".artifacts\release-gate\$stamp"
}

$logRoot = Join-Path $OutputRoot "logs"
$reportRoot = Join-Path $OutputRoot "report"
$sbomRoot = Join-Path $OutputRoot "sbom"

New-Item -ItemType Directory -Force -Path $OutputRoot | Out-Null
New-Item -ItemType Directory -Force -Path $logRoot | Out-Null
New-Item -ItemType Directory -Force -Path $reportRoot | Out-Null

Write-Host "[release-gate] repository: $repoRoot"
Write-Host "[release-gate] artifacts:  $OutputRoot"
Write-Host "[release-gate] shell:      $script:CommandShell"
Write-Host "[release-gate] dotnet:     $((Get-Command dotnet).Source)"

Push-Location $repoRoot

try {
  $steps = @()
  $securityOutputRoot = Join-Path $OutputRoot "security"

  $steps += New-Step -Name "dotnet-info" -Command "dotnet --info" -LogPath (Join-Path $logRoot "dotnet-info.log")
  $steps += New-Step -Name "restore" -Command "dotnet restore STIGForge.sln --nologo -p:EnableWindowsTargeting=true" -LogPath (Join-Path $logRoot "restore.log")
  $steps += New-Step -Name "build" -Command "dotnet build STIGForge.sln --configuration $Configuration --nologo --no-restore -p:EnableWindowsTargeting=true" -LogPath (Join-Path $logRoot "build.log")
  $steps += New-Step -Name "security-gate" -Command "$script:CommandShell -NoProfile -ExecutionPolicy Bypass -File .\tools\release\Invoke-SecurityGate.ps1 -OutputRoot '$securityOutputRoot'" -LogPath (Join-Path $logRoot "security-gate.log")
  $steps += New-Step -Name "test-unit" -Command "dotnet test tests/STIGForge.UnitTests/STIGForge.UnitTests.csproj --configuration $Configuration --framework net8.0 --nologo --no-build --filter 'FullyQualifiedName!~RebootCoordinator'" -LogPath (Join-Path $logRoot "test-unit.log")
  $steps += New-Step -Name "test-integration" -Command "dotnet test tests/STIGForge.IntegrationTests/STIGForge.IntegrationTests.csproj --configuration $Configuration --framework net8.0 --nologo --no-build --filter 'FullyQualifiedName!~E2E'" -LogPath (Join-Path $logRoot "test-integration.log")
  $steps += New-Step -Name "test-e2e" -Command "dotnet test tests/STIGForge.IntegrationTests/STIGForge.IntegrationTests.csproj --configuration $Configuration --framework net8.0 --nologo --no-build --filter 'FullyQualifiedName~E2E'" -LogPath (Join-Path $logRoot "test-e2e.log")

  $failed = @($steps | Where-Object { -not $_.Succeeded })

  $sbomStatus = "skipped"
  $sbomMessage = "SBOM generation skipped by default"
  $sbomPath = ""

  if (-not $SkipSbom -and $failed.Count -eq 0) {
    New-Item -ItemType Directory -Force -Path $sbomRoot | Out-Null
    $sbomPath = Join-Path $sbomRoot "dotnet-packages.json"
    $sbomStep = New-Step -Name "sbom-dotnet-list-package" -Command "dotnet list STIGForge.sln package --include-transitive --format json" -LogPath (Join-Path $logRoot "sbom-dotnet-list-package.log")
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

  $reportPath = Join-Path $reportRoot "release-gate-report.md"
  $summaryPath = Join-Path $reportRoot "release-gate-summary.json"
  $checksumsPath = Join-Path $reportRoot "sha256-checksums.txt"

  $overallPass = ($failed.Count -eq 0)

  $report = New-Object System.Text.StringBuilder
  [void]$report.AppendLine("# STIGForge Release Gate")
  [void]$report.AppendLine()
  [void]$report.AppendLine("- Timestamp (UTC): $(Get-Date -Format 'yyyy-MM-ddTHH:mm:ssZ')")
  [void]$report.AppendLine("- Configuration: $Configuration")
  [void]$report.AppendLine("- Repository: $repoRoot")
  [void]$report.AppendLine("- Result: $(if ($overallPass) { 'PASS' } else { 'FAIL' })")
  [void]$report.AppendLine("- SBOM: $sbomStatus ($sbomMessage)")
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
  [void]$report.AppendLine("- Validate upgrade and rollback path from previous release")
  [void]$report.AppendLine("- Validate VM smoke matrix (Win10/Win11/Server)")
  [void]$report.AppendLine("- Publish release notes, checksums, and SBOM artifacts")

  Set-Content -Path $reportPath -Value $report.ToString() -Encoding UTF8

  $summary = [pscustomobject]@{
    generatedAtUtc = (Get-Date).ToUniversalTime().ToString("o")
    configuration = $Configuration
    repository = $repoRoot
    outputRoot = $OutputRoot
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
