param(
  [string]$Configuration = "Release",
  [string]$Runtime = "win-x64",
  [string]$OutputRoot = "",
  [string]$VersionTag = "",
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

  $baseUri = [System.Uri]((Resolve-Path $BasePath).Path + [IO.Path]::DirectorySeparatorChar)
  $targetUri = [System.Uri]((Resolve-Path $TargetPath).Path)
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
    [Parameter(Mandatory = $true)][string]$OutputPath
  )

  $lines = @()
  foreach ($file in Get-ChildItem -Path $Root -Recurse -File) {
    $hash = Get-FileHash -Algorithm SHA256 -Path $file.FullName
    $relative = Convert-ToRelativePath -BasePath $Root -TargetPath $file.FullName
    $lines += "{0}  {1}" -f $hash.Hash.ToLowerInvariant(), $relative
  }

  $lines | Sort-Object | Set-Content -Path $OutputPath -Encoding UTF8
}

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Resolve-Path (Join-Path $scriptRoot "..\..")
$stamp = Get-Date -Format "yyyyMMdd-HHmmss"

if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
  $OutputRoot = Join-Path $repoRoot ".artifacts\release-package\$stamp"
}

$outputRootFull = [IO.Path]::GetFullPath($OutputRoot)
$logsRoot = Join-Path $outputRootFull "logs"
$publishRoot = Join-Path $outputRootFull "publish"
$bundleRoot = Join-Path $outputRootFull "bundle"
$manifestRoot = Join-Path $outputRootFull "manifest"

New-Item -ItemType Directory -Path $outputRootFull -Force | Out-Null
New-Item -ItemType Directory -Path $logsRoot -Force | Out-Null
New-Item -ItemType Directory -Path $publishRoot -Force | Out-Null
New-Item -ItemType Directory -Path $bundleRoot -Force | Out-Null
New-Item -ItemType Directory -Path $manifestRoot -Force | Out-Null

$cliPublishDir = Join-Path $publishRoot "cli-$Runtime"
$appPublishDir = Join-Path $publishRoot "app-$Runtime"

$signingCertPath = ""
$importedCertThumbprint = ""
$signingPerformed = $false

Write-Host "[package] repository: $repoRoot"
Write-Host "[package] output:     $outputRootFull"

Push-Location $repoRoot

try {
  Invoke-DotnetStep -Name "restore" -Arguments @("restore", "STIGForge.sln", "--nologo", "--runtime", $Runtime) -LogPath (Join-Path $logsRoot "restore.log")
  Invoke-DotnetStep -Name "build" -Arguments @("build", "STIGForge.sln", "--configuration", $Configuration, "--nologo", "--no-restore") -LogPath (Join-Path $logsRoot "build.log")

  Invoke-DotnetStep -Name "publish-cli" -Arguments @("publish", "src/STIGForge.Cli/STIGForge.Cli.csproj", "--configuration", $Configuration, "--runtime", $Runtime, "--self-contained", "false", "-o", $cliPublishDir) -LogPath (Join-Path $logsRoot "publish-cli.log")
  Invoke-DotnetStep -Name "publish-app" -Arguments @("publish", "src/STIGForge.App/STIGForge.App.csproj", "--configuration", $Configuration, "--runtime", $Runtime, "--self-contained", "false", "-o", $appPublishDir) -LogPath (Join-Path $logsRoot "publish-app.log")

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

  if (Test-Path $cliZipPath) { Remove-Item -Path $cliZipPath -Force }
  if (Test-Path $appZipPath) { Remove-Item -Path $appZipPath -Force }
  Compress-Archive -Path (Join-Path $cliPublishDir "*") -DestinationPath $cliZipPath
  Compress-Archive -Path (Join-Path $appPublishDir "*") -DestinationPath $appZipPath

  $checksumsPath = Join-Path $manifestRoot "sha256-checksums.txt"
  Write-Checksums -Root $outputRootFull -OutputPath $checksumsPath

  $manifestPath = Join-Path $manifestRoot "release-package-manifest.json"
  $manifest = [pscustomobject]@{
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
    artifacts = [pscustomobject]@{
      cliPublish = $cliPublishDir
      appPublish = $appPublishDir
      cliZip = $cliZipPath
      appZip = $appZipPath
      checksums = $checksumsPath
    }
  }
  $manifest | ConvertTo-Json -Depth 6 | Set-Content -Path $manifestPath -Encoding UTF8

  Write-Host "[package] cli zip:    $cliZipPath" -ForegroundColor Green
  Write-Host "[package] app zip:    $appZipPath" -ForegroundColor Green
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
