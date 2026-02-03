<#
STIGForge Repo Scaffold (WPF/.NET 8 + net48 core)
- Creates solution + projects + references
- Adds key NuGet packages
- Writes starter code files (MVVM + Host/DI + logging)
- Adds Core models/interfaces stubs + Infrastructure stubs
- Adds CLI skeleton + unit tests
#>

param(
  [string]$RepoName = "stigforge",
  [switch]$InPlace
)

$ErrorActionPreference = "Stop"

function Write-TextFile {
  param(
    [Parameter(Mandatory)] [string]$Path,
    [Parameter(Mandatory)] [string]$Content
  )
  $dir = Split-Path -Parent $Path
  if ($dir -and -not (Test-Path $dir)) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }
  Set-Content -Path $Path -Value $Content -Encoding UTF8
}

function Exec {
  param([string]$Cmd)
  Write-Host ">> $Cmd" -ForegroundColor Cyan
  & powershell -NoProfile -ExecutionPolicy Bypass -Command $Cmd
  if ($LASTEXITCODE -ne 0) { throw "Command failed: $Cmd" }
}

# --- Create repo root ---
if (-not $InPlace) {
  if (Test-Path $RepoName) { throw "Folder '$RepoName' already exists." }
  New-Item -ItemType Directory -Path $RepoName | Out-Null
  Push-Location $RepoName
} else {
  $RepoName = (Get-Location).Path
}

# --- Baseline files ---
Write-TextFile -Path "global.json" -Content @'
{
  "sdk": {
    "version": "8.0.0",
    "rollForward": "latestFeature"
  }
}
'@

Write-TextFile -Path "Directory.Build.props" -Content @'
<Project>
  <PropertyGroup>
    <LangVersion>latest</LangVersion>
    <Nullable>enable</Nullable>
    <TreatWarningsAsErrors>false</TreatWarningsAsErrors>
    <Deterministic>true</Deterministic>
    <ContinuousIntegrationBuild Condition="'$(CI)' == 'true'">true</ContinuousIntegrationBuild>
  </PropertyGroup>
</Project>
'@

Write-TextFile -Path ".editorconfig" -Content @'
root = true

[*]
charset = utf-8
end_of_line = crlf
insert_final_newline = true
indent_style = space
indent_size = 2

[*.cs]
indent_size = 2
dotnet_style_qualification_for_field = false:suggestion
dotnet_style_qualification_for_property = false:suggestion
dotnet_style_qualification_for_method = false:suggestion
dotnet_style_qualification_for_event = false:suggestion
csharp_style_var_for_built_in_types = true:suggestion
csharp_style_var_when_type_is_apparent = true:suggestion
csharp_style_var_elsewhere = true:suggestion
'@

Write-TextFile -Path "README.md" -Content @'
# STIGForge

Offline-first Windows hardening platform: **Build -> Apply -> Verify -> Prove**.

## Quick start
1) Install .NET 8 SDK
2) Run app:
```powershell
dotnet run --project .\src\STIGForge.App\STIGForge.App.csproj
