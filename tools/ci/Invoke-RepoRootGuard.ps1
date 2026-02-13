param(
  [string]$RepoRoot = "."
)

$ErrorActionPreference = "Stop"

$resolvedRoot = (Resolve-Path -LiteralPath $RepoRoot).Path
Push-Location $resolvedRoot

try {
  $allowedRootFiles = @(
    ".editorconfig",
    ".gitignore",
    "CHANGELOG.md",
    "Directory.Build.props",
    "global.json",
    "NuGet.config",
    "README.md",
    "STIGForge.sln",
    "Template_AnswerFile.xml"
  )

  $trackedFiles = @(git ls-files)
  $rootTrackedFiles = @($trackedFiles | Where-Object { $_ -notmatch "/" })

  $unexpected = @($rootTrackedFiles | Where-Object { $_ -notin $allowedRootFiles } | Sort-Object -Unique)
  if ($unexpected.Count -gt 0) {
    Write-Host "Unexpected tracked root-level files:" -ForegroundColor Red
    $unexpected | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
    throw "Repo root guard failed. Move non-runtime artifacts under docs/, tools/, or project folders."
  }

  Write-Host "Repo root guard passed. Tracked root files are within policy." -ForegroundColor Green
}
finally {
  Pop-Location
}
