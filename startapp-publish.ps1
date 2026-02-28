param(
  [ValidateSet("Debug", "Release")]
  [string]$Configuration = "Release",
  [string]$Runtime = "win-x64",
  [string]$ProjectPath = "src/STIGForge.App/STIGForge.App.csproj",
  [string]$OutputRoot = "",
  [switch]$SelfContained,
  [switch]$NoRestore
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = (Resolve-Path $scriptRoot).Path
$projectFullPath = [IO.Path]::GetFullPath((Join-Path $repoRoot $ProjectPath))

if (-not (Test-Path -LiteralPath $projectFullPath)) {
  throw "Project file not found: $projectFullPath"
}

if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
  $stamp = Get-Date -Format "yyyyMMdd-HHmmss"
  $OutputRoot = Join-Path $repoRoot ".artifacts\app-publish\$stamp"
}

$outputRootFull = [IO.Path]::GetFullPath($OutputRoot)
New-Item -ItemType Directory -Path $outputRootFull -Force | Out-Null

Push-Location $repoRoot

try {
  $dotnetArgs = @(
    "publish"
    $projectFullPath
    "--configuration"
    $Configuration
    "--runtime"
    $Runtime
    "--self-contained"
    $SelfContained.IsPresent.ToString().ToLowerInvariant()
    "-p:EnableWindowsTargeting=true"
    "-o"
    $outputRootFull
  )

  if ($NoRestore) {
    $dotnetArgs += "--no-restore"
  }

  Write-Host "[startapp-publish] Publishing STIGForge.App" -ForegroundColor Cyan
  Write-Host "[startapp-publish] Configuration: $Configuration"
  Write-Host "[startapp-publish] Runtime: $Runtime"
  Write-Host "[startapp-publish] Self-contained: $($SelfContained.IsPresent)"
  Write-Host "[startapp-publish] Output: $outputRootFull"

  & dotnet @dotnetArgs
  $exitCode = $LASTEXITCODE
  if ($exitCode -ne 0) {
    throw "dotnet publish failed with exit code $exitCode"
  }

  Write-Host "[startapp-publish] Publish completed" -ForegroundColor Green
}
finally {
  Pop-Location
}
