param(
  [ValidateSet("Debug", "Release")]
  [string]$Configuration = "Debug",
  [string]$ProjectPath = "src/STIGForge.App/STIGForge.App.csproj",
  [switch]$NoRestore,
  [string[]]$AppArgs = @()
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = (Resolve-Path $scriptRoot).Path
$projectFullPath = [IO.Path]::GetFullPath((Join-Path $repoRoot $ProjectPath))

if (-not (Test-Path -LiteralPath $projectFullPath)) {
  throw "Project file not found: $projectFullPath"
}

Push-Location $repoRoot

try {
  $dotnetArgs = @(
    "run"
    "--project"
    $projectFullPath
    "--configuration"
    $Configuration
    "--framework"
    "net8.0-windows"
    "-p:EnableWindowsTargeting=true"
  )

  if ($NoRestore) {
    $dotnetArgs += "--no-restore"
  }

  if ($AppArgs.Count -gt 0) {
    $dotnetArgs += "--"
    $dotnetArgs += $AppArgs
  }

  Write-Host "[startapp] Running STIGForge.App" -ForegroundColor Cyan
  Write-Host "[startapp] Configuration: $Configuration"
  Write-Host "[startapp] Project: $projectFullPath"

  & dotnet @dotnetArgs
  $exitCode = $LASTEXITCODE
  if ($exitCode -ne 0) {
    throw "dotnet run failed with exit code $exitCode"
  }
}
finally {
  Pop-Location
}
